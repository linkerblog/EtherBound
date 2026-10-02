using EtherBound.Llm;

namespace EtherBound.Sim.Tests;

/// <summary>
/// The shipped settings with every role's default model emptied. Most tests are about what a role does with no model, or
/// with one they give it, and must not change when the shipped default model does.
/// </summary>
internal static class NoModel
{
    public static LlmSettings Settings { get; } = Strip(LlmSettings.Load());

    public static LlmSettings Strip(LlmSettings settings) =>
        settings with { Roles = settings.Roles.ToDictionary(p => p.Key, p => p.Value with { Model = "" }) };
}
