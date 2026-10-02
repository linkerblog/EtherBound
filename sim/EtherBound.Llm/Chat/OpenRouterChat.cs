using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EtherBound.Llm.Chat;

/// <summary>
/// A streamed OpenRouter <c>chat/completions</c> call over server-sent events. The key goes only into the
/// Authorization header and is scrubbed from every message it could reach. <c>reasoning</c> is sent only
/// when the role asks for it, and the idle timeout is re-armed on every chunk.
/// </summary>
public sealed class OpenRouterChat : IChatModel
{
    private readonly HttpClient _http;
    private readonly string _key;
    private readonly string _endpoint;

    public OpenRouterChat(HttpClient http, string key, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("an OpenRouter key is required", nameof(key));
        _http = http;
        _key = key.Trim();
        _endpoint = endpoint;
    }

    internal static JsonObject BuildRequest(ChatRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = new JsonArray(request.Messages.Select(m => (JsonNode)new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content,
            }).ToArray()),
            ["stream"] = true,
            ["max_tokens"] = request.MaxTokens,
            // Without this the stream carries no cost, and the spend cap would have nothing to count.
            ["usage"] = new JsonObject { ["include"] = true },
        };
        if (request.Reasoning) body["reasoning"] = new JsonObject { ["enabled"] = true };
        return body;
    }

    public async Task<ChatResult> Complete(ChatRequest request, Action<string>? onDelta, CancellationToken ct = default)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(request.IdleTimeout);
        var text = new StringBuilder();
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(BuildRequest(request).ToJsonString(), Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
            message.Headers.Add("X-Title", "EtherBound");
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(idle.Token).ConfigureAwait(false);
                return ChatResult.Failed(Scrub($"OpenRouter answered HTTP {(int)response.StatusCode}: {Truncate(body, 200)}"));
            }

            using var stream = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            int tokensIn = 0, tokensOut = 0;
            double? cost = null;
            string? finishReason = null;
            var finished = false;
            while (await reader.ReadLineAsync(idle.Token).ConfigureAwait(false) is { } line)
            {
                idle.CancelAfter(request.IdleTimeout);
                // Blank lines separate events and ": comment" lines are keep-alives.
                if (line.Length == 0 || line[0] == ':' || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var payload = line[5..].Trim();
                if (payload == "[DONE]")
                {
                    finished = true;
                    break;
                }
                var chunk = JsonNode.Parse(payload) as JsonObject;
                if (chunk is null) continue;
                if (chunk["error"] is { } error)
                    return ChatResult.Failed(Scrub($"OpenRouter reported an error: {Truncate(error["message"]?.GetValue<string>() ?? error.ToJsonString(), 200)}"),
                        text.ToString());
                if (chunk["choices"]?[0]?["delta"]?["content"] is JsonValue piece && piece.TryGetValue<string>(out var delta) && delta.Length > 0)
                {
                    text.Append(delta);
                    onDelta?.Invoke(delta);
                }
                if (chunk["choices"]?[0]?["finish_reason"] is JsonValue reason && reason.TryGetValue<string>(out var why)) finishReason = why;
                if (chunk["usage"] is JsonObject usage)
                {
                    tokensIn = usage["prompt_tokens"]?.GetValue<int>() ?? tokensIn;
                    tokensOut = usage["completion_tokens"]?.GetValue<int>() ?? tokensOut;
                    cost = usage["cost"]?.GetValue<double>() ?? cost;
                }
            }
            return finished
                ? new ChatResult(text.ToString(), tokensIn, tokensOut, cost, FinishReason: finishReason)
                : ChatResult.Failed("the stream ended before OpenRouter finished", text.ToString());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ChatResult.Failed($"OpenRouter was idle for {request.IdleTimeout.TotalSeconds:0} s", text.ToString());
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException
                                          or FormatException or IOException)
        {
            return ChatResult.Failed(Scrub($"OpenRouter call failed: {error.GetType().Name}: {error.Message}"), text.ToString());
        }
    }

    /// <summary>
    /// One call to the key-info endpoint, which spends no tokens: 200 means OpenRouter knows the key, 401 or 403 that it
    /// does not. The key is scrubbed from every message.
    /// </summary>
    public async Task<KeyTestResult> CheckKey(string endpoint, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, endpoint);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
            using var response = await _http.SendAsync(message, timeout.Token).ConfigureAwait(false);
            return (int)response.StatusCode switch
            {
                >= 200 and < 300 => new KeyTestResult(true, "OpenRouter accepted the key"),
                401 or 403 => new KeyTestResult(false, $"OpenRouter rejected the key (HTTP {(int)response.StatusCode})"),
                var other => new KeyTestResult(false, $"OpenRouter answered HTTP {other}"),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new KeyTestResult(false, "OpenRouter did not answer in 15 s");
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            return new KeyTestResult(false, Scrub($"OpenRouter call failed: {error.GetType().Name}: {error.Message}"));
        }
    }

    private string Scrub(string message) => message.Replace(_key, "[key]", StringComparison.Ordinal);

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];
}
