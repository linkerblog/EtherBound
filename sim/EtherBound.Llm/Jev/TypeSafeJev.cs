using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EtherBound.Llm.Jev;

/// <summary>
/// Jev over TypeSafe's hosted HTTP API (<c>POST /v1/systemone</c> with <c>{ state, model, questions }</c>).
/// Nothing here is a library to port: it is the same small request NikoStory's <c>typesafe.ts</c> makes.
/// The key goes only into the Authorization header and is scrubbed from every message it could reach.
/// </summary>
public sealed class TypeSafeJev : IJev
{
    private readonly HttpClient _http;
    private readonly string _key;
    private readonly string _endpoint;
    private readonly double _pricePerToken;
    private readonly TimeSpan _timeout;

    public TypeSafeJev(HttpClient http, string key, string model, string endpoint, double pricePerMillionInput, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("a Jev key is required", nameof(key));
        _http = http;
        _key = key.Trim();
        Model = model;
        _endpoint = endpoint;
        _pricePerToken = pricePerMillionInput / 1_000_000;
        _timeout = timeout;
    }

    public string Model { get; }
    public bool Configured => true;

    internal static JsonObject BuildRequest(string state, string model, IReadOnlyDictionary<string, JevQuestion> questions)
    {
        var body = new JsonObject { ["state"] = state, ["model"] = model };
        var list = new JsonObject();
        foreach (var (id, question) in questions)
        {
            switch (question)
            {
                case ChoiceQuestion choice:
                    var criteria = new JsonObject();
                    foreach (var option in choice.Options) criteria[option.Name] = option.Description;
                    list[id] = new JsonObject { ["type"] = "choice", ["instructions"] = choice.Instructions, ["criteria"] = criteria };
                    break;
                case NoulQuestion noul:
                    var node = new JsonObject { ["type"] = "noul", ["instructions"] = noul.Instructions };
                    if (noul.WhenTrue is not null || noul.WhenFalse is not null)
                        node["criteria"] = new JsonObject { ["true"] = noul.WhenTrue, ["false"] = noul.WhenFalse };
                    list[id] = node;
                    break;
                default:
                    throw new ArgumentException($"unsupported question type {question.GetType().Name}");
            }
        }
        body["questions"] = list;
        return body;
    }

    internal JevResult ParseResponse(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("empty body");
        var answers = new Dictionary<string, JevAnswer>();
        if (root["answers"] is JsonObject raw)
            foreach (var (id, node) in raw)
            {
                if (node is not JsonObject answer) continue;
                switch (answer["type"]?.GetValue<string>())
                {
                    case "choice" when answer["choice"] is { } picked:
                        var probabilities = new Dictionary<string, double>();
                        if (answer["probabilities"] is JsonObject distribution)
                            foreach (var (name, p) in distribution) probabilities[name] = p!.GetValue<double>();
                        answers[id] = new ChoiceAnswer(picked.GetValue<string>(), answer["confidence"]?.GetValue<double>() ?? 0, probabilities);
                        break;
                    case "noul" when answer["noul"] is { } probability:
                        answers[id] = new NoulAnswer(probability.GetValue<double>());
                        break;
                }
            }
        var tokensIn = root["usage"]?["input_tokens"]?.GetValue<int>() ?? 0;
        var tokensOut = root["usage"]?["output_tokens"]?.GetValue<int>() ?? 0;
        return new JevResult(root["model"]?.GetValue<string>() ?? Model, answers, tokensIn, tokensOut, tokensIn * _pricePerToken);
    }

    public async Task<JevResult> Ask(string state, IReadOnlyDictionary<string, JevQuestion> questions, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(BuildRequest(state, Model, questions).ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return JevResult.Unavailable(Model, Scrub($"Jev answered HTTP {(int)response.StatusCode}: {Truncate(text, 200)}"));
            return ParseResponse(text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return JevResult.Unavailable(Model, $"Jev timed out after {_timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s");
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException
                                          or FormatException or KeyNotFoundException)
        {
            return JevResult.Unavailable(Model, Scrub($"Jev call failed: {error.GetType().Name}: {error.Message}"));
        }
    }

    private string Scrub(string message) => message.Replace(_key, "[key]", StringComparison.Ordinal);

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];
}
