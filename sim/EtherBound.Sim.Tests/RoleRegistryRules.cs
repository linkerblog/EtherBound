using System.Text.Json.Nodes;
using EtherBound.Llm;

namespace EtherBound.Sim.Tests;

public sealed class RoleRegistryRules : IDisposable
{
    private static readonly LlmSettings Settings = NoModel.Settings;
    private readonly string _directory = Directory.CreateTempSubdirectory("etherbound-roles").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private RoleRegistry Registry(Func<string, string?>? environment = null)
    {
        var config = LlmConfig.Load(_directory, _ => null);
        return new RoleRegistry(Settings.Roles, config.Roles, _directory, environment ?? (_ => null));
    }

    [Fact]
    public void The_shipped_default_narrator_model_is_the_one_the_bench_chose_and_reasoning_is_off()
    {
        var shipped = LlmSettings.Load().Roles["narrator"];

        Assert.Equal("google/gemma-4-31b-it", shipped.Model);
        Assert.False(shipped.Reasoning);
        Assert.Equal(1000, shipped.MaxTokens);
    }

    [Fact]
    public void A_role_with_no_default_model_is_off_and_reasoning_is_off()
    {
        var (config, source) = Registry().Describe("narrator");

        Assert.False(config.Enabled);
        Assert.False(config.Reasoning);
        Assert.Equal("default", source);
    }

    [Fact]
    public void The_tab_beats_the_environment_which_beats_the_default()
    {
        var registry = Registry(name => name == "ETHERBOUND_NARRATOR_MODEL" ? "env/model" : null);
        Assert.Equal(("env/model", "env"), (registry.Resolve("narrator").Model, registry.Describe("narrator").ModelSource));

        registry.Set("narrator", new RoleOverride(Model: "tab/model"));
        Assert.Equal(("tab/model", "tab"), (registry.Resolve("narrator").Model, registry.Describe("narrator").ModelSource));

        registry.Clear("narrator");
        Assert.Equal("env/model", registry.Resolve("narrator").Model);
    }

    [Fact]
    public void An_edit_applies_to_the_next_resolve_and_survives_a_reload_the_way_a_new_game_does()
    {
        var registry = Registry();
        registry.Set("narrator", new RoleOverride(Model: " some/model ", Reasoning: true, IdleMs: 45_000));

        var after = Registry().Resolve("narrator");

        Assert.Equal(("some/model", true, 45_000), (after.Model, after.Reasoning, after.IdleMs));
        Assert.Equal(1000, after.MaxTokens);
    }

    [Fact]
    public void Edits_merge_field_by_field_and_the_idle_timeout_is_clamped()
    {
        var registry = Registry();
        registry.Set("narrator", new RoleOverride(Model: "a/b"));
        registry.Set("narrator", new RoleOverride(Reasoning: true));
        registry.Set("narrator", new RoleOverride(IdleMs: 5));

        var config = registry.Resolve("narrator");

        Assert.Equal(("a/b", true, RoleRegistry.MinIdleMs), (config.Model, config.Reasoning, config.IdleMs));
        registry.Set("narrator", new RoleOverride(IdleMs: int.MaxValue));
        Assert.Equal(RoleRegistry.MaxIdleMs, registry.Resolve("narrator").IdleMs);
    }

    [Fact]
    public void An_unknown_role_is_refused()
    {
        var registry = Registry();

        Assert.Throws<KeyNotFoundException>(() => registry.Resolve("oracle"));
        Assert.Throws<KeyNotFoundException>(() => registry.Set("oracle", new RoleOverride(Model: "x")));
    }

    [Fact]
    public void Saving_roles_keeps_the_keys_in_the_file_and_never_writes_one_it_did_not_have()
    {
        File.WriteAllText(Path.Combine(_directory, LlmConfig.FileName), """{"typesafe_key":"tk-1","openrouter_key":"ok-2","jev_model":"jev-x"}""");

        Registry().Set("narrator", new RoleOverride(Model: "some/model"));

        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(_directory, LlmConfig.FileName)))!.AsObject();
        Assert.Equal("tk-1", saved["typesafe_key"]!.GetValue<string>());
        Assert.Equal("ok-2", saved["openrouter_key"]!.GetValue<string>());
        Assert.Equal("jev-x", saved["jev_model"]!.GetValue<string>());
        Assert.Equal("some/model", saved["roles"]!["narrator"]!["model"]!.GetValue<string>());

        var fresh = Directory.CreateTempSubdirectory("etherbound-nokeys").FullName;
        try
        {
            LlmConfig.SaveRoles(fresh, new Dictionary<string, RoleOverride> { ["narrator"] = new(Model: "m") });
            var text = File.ReadAllText(Path.Combine(fresh, LlmConfig.FileName));
            Assert.DoesNotContain("key", text);
        }
        finally
        {
            Directory.Delete(fresh, recursive: true);
        }
    }

    [Fact]
    public void Config_reads_the_openrouter_key_from_the_file_first_then_the_environment()
    {
        File.WriteAllText(Path.Combine(_directory, LlmConfig.FileName), """{"openrouter_key":"from-file"}""");

        Assert.Equal("from-file", LlmConfig.Load(_directory, _ => null).OpenRouterKey);
        Assert.Equal("from-file", LlmConfig.Load(_directory, n => n == LlmConfig.OpenRouterKeyVariable ? "from-env" : null).OpenRouterKey);
        File.Delete(Path.Combine(_directory, LlmConfig.FileName));
        Assert.Equal("from-env", LlmConfig.Load(_directory, n => n == LlmConfig.OpenRouterKeyVariable ? "from-env" : null).OpenRouterKey);
    }
}
