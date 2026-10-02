using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EtherBound.Llm;
using EtherBound.Llm.Chat;

namespace EtherBound.Sim.Tests;

/// <summary>Tests that measure real time run alone: beside the host tests a timer can starve for a second.</summary>
[CollectionDefinition("Timing", DisableParallelization = true)]
public sealed class TimingCollection;

[Collection("Timing")]
public sealed class ChatClientRules
{
    private const string Key = "sk-or-secret-456";

    private static OpenRouterChat Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Key, "https://router.test/api/v1/chat/completions");

    private static ChatRequest Request(bool reasoning = false, int idleMs = 5000) => new("some/model",
        new[] { new ChatMessage("system", "be brief"), new ChatMessage("user", "hello") }, reasoning, 200, TimeSpan.FromMilliseconds(idleMs));

    private static string Sse(params string[] data) => string.Concat(data.Select(d => $"data: {d}\n\n"));

    private static HttpResponseMessage Stream(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
    };

    private static string Delta(string text) =>
        new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject { ["content"] = text } }) }.ToJsonString();

    [Fact]
    public async Task Parses_a_stream_into_deltas_text_tokens_and_the_providers_cost()
    {
        var handler = new FakeHandler(_ => Stream(": OPENROUTER PROCESSING\n\n" + Sse(
            Delta("Niko "), Delta("digs."),
            """{"choices":[{"delta":{}}],"usage":{"prompt_tokens":120,"completion_tokens":7,"cost":0.00042}}""",
            "[DONE]")));
        var pieces = new List<string>();

        var result = await Client(handler).Complete(Request(), pieces.Add);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("Niko digs.", result.Text);
        Assert.Equal(new[] { "Niko ", "digs." }, pieces);
        Assert.Equal((120, 7), (result.TokensIn, result.TokensOut));
        Assert.Equal(0.00042, result.CostUsd);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("length")]
    public async Task The_finish_reason_is_kept_so_a_cut_off_answer_can_be_told_from_a_finished_one(string reason)
    {
        var finish = new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject(), ["finish_reason"] = reason }) }.ToJsonString();

        var result = await Client(new FakeHandler(_ => Stream(Sse(Delta("Niko "), finish, "[DONE]")))).Complete(Request(), null);

        Assert.True(result.Ok);
        Assert.Equal(reason, result.FinishReason);
    }

    [Fact]
    public async Task A_missing_cost_stays_null_rather_than_becoming_zero()
    {
        var result = await Client(new FakeHandler(_ => Stream(Sse(Delta("Hi."), "[DONE]")))).Complete(Request(), null);

        Assert.True(result.Ok);
        Assert.Null(result.CostUsd);
    }

    [Fact]
    public async Task Sends_the_bearer_key_the_model_the_messages_and_asks_for_usage()
    {
        var handler = new FakeHandler(_ => Stream(Sse(Delta("Hi."), "[DONE]")));

        await Client(handler).Complete(Request(), null);

        Assert.Equal(Key, handler.Request!.Headers.Authorization!.Parameter);
        Assert.Equal("https://router.test/api/v1/chat/completions", handler.Request.RequestUri!.ToString());
        var body = JsonNode.Parse(handler.Body!)!.AsObject();
        Assert.Equal("some/model", body["model"]!.GetValue<string>());
        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.Equal(200, body["max_tokens"]!.GetValue<int>());
        Assert.True(body["usage"]!["include"]!.GetValue<bool>());
        Assert.Equal(new[] { "system", "user" }, body["messages"]!.AsArray().Select(m => m!["role"]!.GetValue<string>()));
        Assert.DoesNotContain(Key, handler.Body);
    }

    [Fact]
    public async Task Reasoning_is_sent_only_when_the_role_asks_for_it()
    {
        var off = new FakeHandler(_ => Stream(Sse(Delta("Hi."), "[DONE]")));
        await Client(off).Complete(Request(reasoning: false), null);
        Assert.Null(JsonNode.Parse(off.Body!)!["reasoning"]);

        var on = new FakeHandler(_ => Stream(Sse(Delta("Hi."), "[DONE]")));
        await Client(on).Complete(Request(reasoning: true), null);
        Assert.True(JsonNode.Parse(on.Body!)!["reasoning"]!["enabled"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(402)]
    [InlineData(500)]
    public async Task An_http_error_fails_and_the_key_never_appears_in_the_message(int status)
    {
        var handler = new FakeHandler(_ => FakeHandler.Json($"bad key {Key}", (HttpStatusCode)status));

        var result = await Client(handler).Complete(Request(), null);

        Assert.False(result.Ok);
        Assert.Contains(status.ToString(), result.Error);
        Assert.DoesNotContain(Key, result.Error);
    }

    [Fact]
    public async Task A_stream_cut_before_done_is_a_failure_even_with_text_so_far()
    {
        var result = await Client(new FakeHandler(_ => Stream(Sse(Delta("Niko ")))))
            .Complete(Request(), null);

        Assert.False(result.Ok);
        Assert.Equal("Niko ", result.Text);
        Assert.Contains("ended", result.Error);
    }

    [Fact]
    public async Task An_error_chunk_in_the_stream_is_a_failure_with_its_message()
    {
        var result = await Client(new FakeHandler(_ => Stream(Sse(Delta("Hi"), """{"error":{"message":"model overloaded"}}""", "[DONE]"))))
            .Complete(Request(), null);

        Assert.False(result.Ok);
        Assert.Contains("model overloaded", result.Error);
    }

    [Fact]
    public async Task A_network_failure_fails_and_scrubs_the_key()
    {
        var result = await Client(new FakeHandler(_ => throw new HttpRequestException($"no route with {Key}"))).Complete(Request(), null);

        Assert.False(result.Ok);
        Assert.DoesNotContain(Key, result.Error);
    }

    [Fact]
    public async Task The_idle_timeout_is_per_chunk_so_a_slow_but_steady_stream_survives()
    {
        // Eight chunks, 150 ms apart: 1.2 s in all against a 1 s idle timeout. Each gap is far under the timeout
        // while the whole stream is over it; the margin is wide on purpose, the suite runs in parallel.
        var result = await Client(new SlowStreamHandler(8, 150)).Complete(Request(idleMs: 1000), null);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("abcdefgh", result.Text);
    }

    [Fact]
    public async Task A_stream_that_goes_quiet_fails_as_idle()
    {
        var result = await Client(new SlowStreamHandler(2, 20, stallAfter: true)).Complete(Request(idleMs: 1500), null)
            .WaitAsync(TimeSpan.FromSeconds(15));

        Assert.False(result.Ok);
        Assert.Contains("idle", result.Error);
        Assert.Equal("ab", result.Text);
    }

    [Fact]
    public async Task Cancelling_the_caller_propagates()
    {
        using var cancel = new CancellationTokenSource();
        var pending = Client(new SlowStreamHandler(2, 60, stallAfter: true)).Complete(Request(idleMs: 60_000), null, cancel.Token);
        cancel.CancelAfter(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    /// <summary>A server-sent stream that writes one letter every <c>gapMs</c> and either ends or stalls.</summary>
    private sealed class SlowStreamHandler : HttpMessageHandler
    {
        private readonly int _chunks, _gapMs;
        private readonly bool _stall;

        public SlowStreamHandler(int chunks, int gapMs, bool stallAfter = false) => (_chunks, _gapMs, _stall) = (chunks, gapMs, stallAfter);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new SlowStream(_chunks, _gapMs, _stall)),
            });
    }

    private sealed class SlowStream : Stream
    {
        private readonly Queue<byte[]> _pieces = new();
        private readonly int _gapMs;
        private readonly bool _stall;
        private byte[] _current = Array.Empty<byte>();
        private int _offset;

        public SlowStream(int chunks, int gapMs, bool stall)
        {
            _gapMs = gapMs;
            _stall = stall;
            for (var i = 0; i < chunks; i++) _pieces.Enqueue(Encoding.UTF8.GetBytes($"data: {Delta(((char)('a' + i)).ToString())}\n\n"));
            if (!stall) _pieces.Enqueue(Encoding.UTF8.GetBytes("data: [DONE]\n\n"));
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset >= _current.Length)
            {
                if (_pieces.Count == 0)
                {
                    if (_stall) await Task.Delay(Timeout.Infinite, cancellationToken);
                    return 0;
                }
                await Task.Delay(_gapMs, cancellationToken);
                _current = _pieces.Dequeue();
                _offset = 0;
            }
            var count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
