using System.Net;
using System.Text.Json.Nodes;
using EtherBound.Llm;
using EtherBound.Llm.Chat;
using EtherBound.Llm.Jev;

namespace EtherBound.Sim.Tests;

/// <summary>Keys typed in the game (Dev-009): saved to llm.json, preferred over the environment, swapped in with no restart.</summary>
public sealed class LlmKeyRules : IDisposable
{
    private const string KeyA = "key-aaaaaaaa-1111";
    private const string KeyB = "key-bbbbbbbb-2222";
    private readonly string _directory = Directory.CreateTempSubdirectory("etherbound-keys").FullName;
    private string FilePath => Path.Combine(_directory, LlmConfig.FileName);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static Func<string, string?> NoEnvironment => _ => null;

    private static Func<string, string?> Environment(string variable, string value) => name => name == variable ? value : null;

    private JsonObject Saved() => JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();

    [Fact]
    public void Saving_a_key_keeps_the_roles_and_the_other_key_and_leaves_no_temporary_file()
    {
        File.WriteAllText(FilePath, """{"openrouter_key":"keep-this-one","jev_model":"jev-x","roles":{"narrator":{"model":"a/b","reasoning":true}}}""");

        LlmConfig.SaveKey(_directory, KeyKind.Jev, KeyA);

        var saved = Saved();
        Assert.Equal(KeyA, saved["typesafe_key"]!.GetValue<string>());
        Assert.Equal("keep-this-one", saved["openrouter_key"]!.GetValue<string>());
        Assert.Equal("jev-x", saved["jev_model"]!.GetValue<string>());
        Assert.Equal("a/b", saved["roles"]!["narrator"]!["model"]!.GetValue<string>());
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Saving_a_role_keeps_the_keys_and_removing_a_key_keeps_the_roles()
    {
        LlmConfig.SaveKey(_directory, KeyKind.OpenRouter, KeyA);
        LlmConfig.SaveRoles(_directory, new Dictionary<string, RoleOverride> { ["narrator"] = new(Model: "m/n") });
        Assert.Equal(KeyA, Saved()["openrouter_key"]!.GetValue<string>());

        LlmConfig.RemoveKey(_directory, KeyKind.OpenRouter);

        Assert.Null(Saved()["openrouter_key"]);
        Assert.Equal("m/n", Saved()["roles"]!["narrator"]!["model"]!.GetValue<string>());
    }

    [Fact]
    public void A_broken_file_is_rebuilt_rather_than_crashed_on()
    {
        File.WriteAllText(FilePath, "{ not json");

        LlmConfig.SaveKey(_directory, KeyKind.Jev, KeyA);

        Assert.Equal(KeyA, Saved()["typesafe_key"]!.GetValue<string>());
    }

    [Fact]
    public void A_saved_key_beats_the_environment_and_removing_it_falls_back_and_each_source_is_reported()
    {
        var environment = Environment(LlmConfig.TypeSafeKeyVariable, "env-key-12345");
        Assert.Equal((KeySource.Environment, "env-key-12345"), (LlmConfig.Load(_directory, environment).TypeSafeKeySource,
            LlmConfig.Load(_directory, environment).TypeSafeKey));

        LlmConfig.SaveKey(_directory, KeyKind.Jev, KeyA);
        var saved = LlmConfig.Load(_directory, environment);
        Assert.Equal((KeySource.Saved, KeyA), (saved.TypeSafeKeySource, saved.TypeSafeKey));
        Assert.Equal(KeySource.None, saved.OpenRouterKeySource);

        LlmConfig.RemoveKey(_directory, KeyKind.Jev);
        var back = LlmConfig.Load(_directory, environment);
        Assert.Equal((KeySource.Environment, "env-key-12345"), (back.TypeSafeKeySource, back.TypeSafeKey));
        Assert.Equal(KeySource.None, LlmConfig.Load(_directory, NoEnvironment).TypeSafeKeySource);
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("short", "shorter")]
    [InlineData("key with a space inside", "space")]
    [InlineData("key-line\nbreak-1234", "space")]
    [InlineData("clave-con-ñ-12345", "ASCII")]
    public void A_key_of_the_wrong_shape_is_refused_with_a_reason_and_never_echoed(string? raw, string why)
    {
        var check = KeyCheck.Validate(raw);

        Assert.False(check.Ok);
        Assert.Equal("", check.Key);
        Assert.Contains(why, check.Reason);
    }

    [Fact]
    public void A_key_that_is_too_long_is_refused_and_a_padded_good_one_is_trimmed()
    {
        Assert.Contains("longer", KeyCheck.Validate(new string('k', KeyCheck.MaxLength + 1)).Reason);
        var padded = KeyCheck.Validate("  " + KeyA + "\n");
        Assert.True(padded.Ok);
        Assert.Equal(KeyA, padded.Key);
    }

    [Fact]
    public void A_refused_key_writes_nothing()
    {
        using var runtime = LlmRuntime.Create(_directory, NoEnvironment);

        var result = runtime.SaveKey(KeyKind.Jev, "no good");

        Assert.False(result.Ok);
        Assert.False(File.Exists(FilePath));
        Assert.False(runtime.Interpreter.Configured);
    }

    [Fact]
    public async Task Saving_a_key_starts_using_it_with_no_restart_and_a_call_in_flight_keeps_the_client_it_started_with()
    {
        var keys = new List<string?>();
        var handler = new FakeHandler(request =>
        {
            keys.Add(request.Headers.Authorization?.Parameter);
            return FakeHandler.Json("""{"answers":{}}""");
        });
        using var runtime = LlmRuntime.Create(_directory, NoEnvironment, handler);
        Assert.False(runtime.Jev.Configured);
        Assert.Equal(KeySource.None, runtime.SourceOf(KeyKind.Jev));

        Assert.True(runtime.SaveKey(KeyKind.Jev, KeyA).Ok);
        var before = runtime.Jev;
        Assert.True(runtime.Interpreter.Configured);
        Assert.Equal(KeySource.Saved, runtime.SourceOf(KeyKind.Jev));
        Assert.True(runtime.SaveKey(KeyKind.Jev, KeyB).Ok);

        await before.Ask("s", new Dictionary<string, JevQuestion>());
        await runtime.Jev.Ask("s", new Dictionary<string, JevQuestion>());

        Assert.Equal(new[] { KeyA, KeyB }, keys);
    }

    [Fact]
    public void Removing_a_key_puts_the_no_key_state_back_and_a_saved_openrouter_key_makes_a_narrator()
    {
        using var runtime = LlmRuntime.Create(_directory, NoEnvironment);
        Assert.False(runtime.ChatConfigured);

        Assert.True(runtime.SaveKey(KeyKind.OpenRouter, KeyA).Ok);
        Assert.True(runtime.ChatConfigured);
        Assert.True(runtime.SaveKey(KeyKind.Jev, KeyB).Ok);

        Assert.True(runtime.RemoveKey(KeyKind.OpenRouter).Ok);
        Assert.False(runtime.ChatConfigured);
        Assert.True(runtime.Interpreter.Configured);
        Assert.True(runtime.RemoveKey(KeyKind.Jev).Ok);
        Assert.False(runtime.Interpreter.Configured);
        Assert.IsType<NoJev>(runtime.Jev);
    }

    [Fact]
    public void Removing_the_saved_key_hands_over_to_the_environments_one()
    {
        using var runtime = LlmRuntime.Create(_directory, Environment(LlmConfig.OpenRouterKeyVariable, "env-key-12345"));
        Assert.Equal(KeySource.Environment, runtime.SourceOf(KeyKind.OpenRouter));

        runtime.SaveKey(KeyKind.OpenRouter, KeyA);
        Assert.Equal(KeySource.Saved, runtime.SourceOf(KeyKind.OpenRouter));
        runtime.RemoveKey(KeyKind.OpenRouter);

        Assert.Equal(KeySource.Environment, runtime.SourceOf(KeyKind.OpenRouter));
        Assert.True(runtime.ChatConfigured);
    }

    [Fact]
    public void A_runtime_with_no_user_folder_cannot_save_a_key_and_says_so()
    {
        using var runtime = new LlmRuntime(LlmSettings.Load(), new NoJev());

        var result = runtime.SaveKey(KeyKind.Jev, KeyA);

        Assert.False(runtime.CanEditKeys);
        Assert.False(result.Ok);
        Assert.Contains("no user folder", result.Reason);
    }

    [Fact]
    public void A_failed_write_says_so_and_changes_nothing()
    {
        // A directory where the file should be makes the move fail.
        Directory.CreateDirectory(FilePath);
        using var runtime = LlmRuntime.Create(_directory, NoEnvironment);

        var result = runtime.SaveKey(KeyKind.Jev, KeyA);

        Assert.False(result.Ok);
        Assert.Contains("could not write", result.Reason);
        Assert.DoesNotContain(KeyA, result.Reason);
        Assert.False(runtime.Interpreter.Configured);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task Testing_the_jev_key_says_ok_or_the_scrubbed_error(HttpStatusCode status, bool ok)
    {
        // The body even echoes the key, as a careless vendor error might.
        var body = new JsonObject
        {
            ["answers"] = new JsonObject { ["test"] = new JsonObject { ["type"] = "noul", ["noul"] = 0.9 } },
            ["note"] = KeyA,
        }.ToJsonString();
        var handler = new FakeHandler(_ => FakeHandler.Json(body, status));
        using var runtime = LlmRuntime.Create(_directory, NoEnvironment, handler);
        runtime.SaveKey(KeyKind.Jev, KeyA);

        var result = await runtime.Test(KeyKind.Jev);

        Assert.Equal(ok, result.Ok);
        Assert.DoesNotContain(KeyA, result.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true, "accepted")]
    [InlineData(HttpStatusCode.Unauthorized, false, "rejected")]
    [InlineData(HttpStatusCode.Forbidden, false, "rejected")]
    [InlineData(HttpStatusCode.BadGateway, false, "502")]
    public async Task Testing_the_openrouter_key_reads_the_key_info_endpoint(HttpStatusCode status, bool ok, string word)
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("{}", status));
        using var runtime = LlmRuntime.Create(_directory, NoEnvironment, handler);
        runtime.SaveKey(KeyKind.OpenRouter, KeyA);

        var result = await runtime.Test(KeyKind.OpenRouter);

        Assert.Equal(ok, result.Ok);
        Assert.Contains(word, result.Message);
        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal(LlmSettings.Load().OpenRouterKeyInfoEndpoint, handler.Request.RequestUri!.ToString());
        Assert.Equal(KeyA, handler.Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task A_network_failure_in_a_test_is_a_message_without_the_key()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException($"no route with {KeyA}"));
        using var runtime = LlmRuntime.Create(_directory, NoEnvironment, handler);
        runtime.SaveKey(KeyKind.OpenRouter, KeyA);
        runtime.SaveKey(KeyKind.Jev, KeyB);

        var chat = await runtime.Test(KeyKind.OpenRouter);
        var jev = await runtime.Test(KeyKind.Jev);

        Assert.False(chat.Ok);
        Assert.False(jev.Ok);
        Assert.DoesNotContain(KeyA, chat.Message);
        Assert.DoesNotContain(KeyB, jev.Message);
    }

    [Fact]
    public async Task Testing_with_no_key_says_so_without_calling_anything()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("{}"));
        using var runtime = LlmRuntime.Create(_directory, NoEnvironment, handler);

        Assert.Equal("no key is set", (await runtime.Test(KeyKind.Jev)).Message);
        Assert.Equal("no key is set", (await runtime.Test(KeyKind.OpenRouter)).Message);
        Assert.Equal(0, handler.Calls);
    }
}
