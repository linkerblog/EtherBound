using System.Collections.Immutable;
using System.Net;
using System.Text.Json.Nodes;
using EtherBound.Llm;
using EtherBound.Llm.Jev;

namespace EtherBound.Sim.Tests;

/// <summary>A scripted HTTP layer: records what was sent and answers with a canned response.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public HttpRequestMessage? Request { get; private set; }
    public string? Body { get; private set; }
    public int Calls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Request = request;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return _respond(request);
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}

public sealed class JevClientRules
{
    private const string Key = "sk-secret-key-123";

    private static TypeSafeJev Client(FakeHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler), Key, "jev-latest", "https://jev.test/v1/systemone", 0.042, timeout ?? TimeSpan.FromSeconds(5));

    private static IReadOnlyDictionary<string, JevQuestion> Questions() => new Dictionary<string, JevQuestion>
    {
        ["pick"] = new ChoiceQuestion("Which?", ImmutableArray.Create(new JevOption("a", "first"), new JevOption("b", null))),
        ["sure"] = new NoulQuestion("Sure?", "yes means yes", "no means no"),
    };

    [Fact]
    public async Task Posts_state_model_and_questions_with_the_bearer_key_and_parses_both_answer_types()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""
            {"model":"jev-latest","answers":{
              "pick":{"type":"choice","choice":"a","confidence":0.9,"probabilities":{"a":0.9,"b":0.1}},
              "sure":{"type":"noul","noul":0.8}},
             "usage":{"input_tokens":1000000,"output_tokens":5}}
            """));

        var result = await Client(handler).Ask("the state", Questions());

        Assert.True(result.Available);
        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal(Key, handler.Request.Headers.Authorization.Parameter);
        Assert.Equal("https://jev.test/v1/systemone", handler.Request.RequestUri!.ToString());
        var body = JsonNode.Parse(handler.Body!)!.AsObject();
        Assert.Equal("the state", body["state"]!.GetValue<string>());
        Assert.Equal("jev-latest", body["model"]!.GetValue<string>());
        var pick = body["questions"]!["pick"]!;
        Assert.Equal("choice", pick["type"]!.GetValue<string>());
        Assert.Equal("first", pick["criteria"]!["a"]!.GetValue<string>());
        Assert.Null(pick["criteria"]!["b"]);
        Assert.Equal("noul", body["questions"]!["sure"]!["type"]!.GetValue<string>());
        Assert.Equal("yes means yes", body["questions"]!["sure"]!["criteria"]!["true"]!.GetValue<string>());

        var choice = Assert.IsType<ChoiceAnswer>(result.Answers["pick"]);
        Assert.Equal("a", choice.Choice);
        Assert.Equal(0.9, choice.Confidence);
        Assert.Equal(0.1, choice.Probabilities["b"]);
        Assert.Equal(0.8, Assert.IsType<NoulAnswer>(result.Answers["sure"]).Noul);
        Assert.Equal(1_000_000, result.TokensIn);
        Assert.Equal(0.042, result.CostUsd, 6);
    }

    [Fact]
    public async Task Choice_options_keep_their_order_in_the_request()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"answers":{}}"""));
        await Client(handler).Ask("s", Questions());

        var names = JsonNode.Parse(handler.Body!)!["questions"]!["pick"]!["criteria"]!.AsObject().Select(p => p.Key).ToArray();
        Assert.Equal(new[] { "a", "b" }, names);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task An_http_error_is_unavailable_and_the_key_never_appears_in_the_message(int status)
    {
        var handler = new FakeHandler(_ => FakeHandler.Json($"bad key {Key} rejected", (HttpStatusCode)status));

        var result = await Client(handler).Ask("s", Questions());

        Assert.False(result.Available);
        Assert.Empty(result.Answers);
        Assert.Contains(status.ToString(), result.Error);
        Assert.DoesNotContain(Key, result.Error);
    }

    [Fact]
    public async Task A_malformed_body_is_unavailable_not_a_crash()
    {
        var result = await Client(new FakeHandler(_ => FakeHandler.Json("not json at all"))).Ask("s", Questions());

        Assert.False(result.Available);
        Assert.DoesNotContain(Key, result.Error);
    }

    [Fact]
    public async Task A_network_failure_is_unavailable_and_scrubs_the_key()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException($"could not reach host with {Key}"));

        var result = await Client(handler).Ask("s", Questions());

        Assert.False(result.Available);
        Assert.DoesNotContain(Key, result.Error);
    }

    [Fact]
    public async Task A_slow_answer_times_out_as_unavailable()
    {
        var slow = new TypeSafeJev(new HttpClient(new SlowHandler()), Key, "jev-latest", "https://jev.test", 0.042,
            TimeSpan.FromMilliseconds(50));
        var timedOut = await slow.Ask("s", Questions()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(timedOut.Available);
        Assert.Contains("timed out", timedOut.Error);
    }

    [Fact]
    public async Task Cancelling_the_caller_propagates_instead_of_reporting_unavailable()
    {
        using var cancel = new CancellationTokenSource();
        var slow = new TypeSafeJev(new HttpClient(new SlowHandler()), Key, "jev-latest", "https://jev.test", 0.042, TimeSpan.FromSeconds(30));
        var pending = slow.Ask("s", Questions(), cancel.Token);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task No_key_means_no_jev_and_never_a_guess()
    {
        var result = await new NoJev().Ask("s", Questions());

        Assert.False(result.Available);
        Assert.Empty(result.Answers);
        Assert.False(new NoJev().Configured);
    }

    [Fact]
    public void Config_prefers_the_saved_key_over_the_environment_and_ignores_a_broken_file()
    {
        var directory = Directory.CreateTempSubdirectory("etherbound-llm").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, LlmConfig.FileName), """{"typesafe_key":"from-file","jev_model":"file-model"}""");
            var fromFile = LlmConfig.Load(directory, _ => null);
            Assert.Equal("from-file", fromFile.TypeSafeKey);
            Assert.Equal("file-model", fromFile.JevModel);

            // A key typed in the game wins (Dev-009 D3); the model still follows the environment first.
            var withEnvironment = LlmConfig.Load(directory, name => name switch
            {
                LlmConfig.TypeSafeKeyVariable => "from-env",
                LlmConfig.JevModelVariable => "env-model",
                _ => null,
            });
            Assert.Equal("from-file", withEnvironment.TypeSafeKey);
            Assert.Equal("env-model", withEnvironment.JevModel);

            File.WriteAllText(Path.Combine(directory, LlmConfig.FileName), "{ not json");
            Assert.Null(LlmConfig.Load(directory, _ => null).TypeSafeKey);
            Assert.Equal("from-env", LlmConfig.Load(directory, name => name == LlmConfig.TypeSafeKeyVariable ? "from-env" : null).TypeSafeKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Runtime_without_a_key_has_no_free_text_and_with_a_key_does()
    {
        using var none = LlmRuntime.Create(null, _ => null);
        Assert.False(none.Interpreter.Configured);

        using var keyed = LlmRuntime.Create(null, name => name == LlmConfig.TypeSafeKeyVariable ? Key : null);
        Assert.True(keyed.Interpreter.Configured);
        Assert.Equal("jev-latest", keyed.Jev.Model);
    }

    [Fact]
    public void Thresholds_load_and_are_ordered()
    {
        var settings = LlmSettings.Load();

        Assert.Equal(0.75, settings.Accept);
        Assert.Equal(0.40, settings.Confirm);
        Assert.Equal(40, settings.MaxCandidates);
        var embedded = LlmSettings.ReadText("llm.toml");
        Assert.Throws<InvalidDataException>(() => LlmSettings.Parse(embedded.Replace("accept = 0.75", "accept = 0.2")));
        Assert.Throws<InvalidDataException>(() => LlmSettings.Parse("[jev]\nmodel = \"m\"\n"));
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
