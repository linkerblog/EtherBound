using System.Diagnostics;
using System.Globalization;
using EtherBound.Llm.Chat;
using EtherBound.Llm.Narration;

namespace EtherBound.Llm.Bench;

/// <summary>
/// The narrator bench of Dev-008 [Sec. 4]: twenty narrations of a fixed session, per candidate model. Needs an OpenRouter
/// key. Pass rule, written before the numbers: p95 time to first token under 2.5 s and fewer than 10 % of the narrations
/// rejected after the retry. The default model in `Data/llm.toml` is the cheapest candidate that passes.
///
///   dotnet run --project sim/EtherBound.Llm.Bench -- narrator --models provider/a,provider/b [--config DIR] [--reasoning]
/// </summary>
public static class NarratorBench
{
    private static readonly string[][] Session =
    {
        new[] { "Niko looks closely: Asphalt, diggable." },
        new[] { "Niko picked up the shovel." },
        new[] { "Niko dug through the asphalt. Under it is gravel." },
        new[] { "Niko dug through the gravel. Under it is topsoil." },
        new[] { "Time passes." },
        new[] { "Niko dropped the shovel." },
        new[] { "Niko looks closely: Chest, Wood, closed." },
        new[] { "Niko opened the chest." },
        new[] { "Niko picked up 3 apple." },
        new[] { "Niko closed the chest." },
        new[] { "Niko hits something hard.", "Time passes." },
        new[] { "Niko pushed the chest." },
        new[] { "Niko built a brick wall." },
        new[] { "Niko laid a wood floor." },
        new[] { "Niko cannot finish dig: out of reach." },
        new[] { "Niko climbs." },
        new[] { "Niko looks closely: Grass." },
        new[] { "Niko threw the bottle." },
        new[] { "Niko picked up the backpack.", "Niko put down the apple." },
        new[] { "Time passes." },
    };

    public static async Task<int> Run(string[] args)
    {
        var models = args.SkipWhile(a => a != "--models").Skip(1).FirstOrDefault()?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var configDirectory = args.SkipWhile(a => a != "--config").Skip(1).FirstOrDefault();
        var reasoning = args.Contains("--reasoning");
        if (models is null or { Length: 0 })
        {
            Console.Error.WriteLine("Name at least one model: --models provider/a,provider/b");
            return 2;
        }
        var settings = LlmSettings.Load();
        var config = LlmConfig.Load(configDirectory);
        if (config.OpenRouterKey is not { } key)
        {
            Console.Error.WriteLine($"No OpenRouter key: set {LlmConfig.OpenRouterKeyVariable} or put openrouter_key in {LlmConfig.FileName} (--config DIR).");
            return 2;
        }
        using var http = new HttpClient();
        var chat = new OpenRouterChat(http, key, settings.OpenRouterEndpoint);
        var anyPassed = false;
        foreach (var model in models)
        {
            // Never the user's saved choices: a fresh registry with the bench's model and nothing written to disk.
            var roles = new RoleRegistry(settings.Roles, new Dictionary<string, RoleOverride>
            {
                [Narrator.Role] = new(model, reasoning),
            }, null, _ => null);
            var narrator = new Narrator(chat, roles, settings, Narrator.DefaultPrompt());
            var first = new List<double>();
            var total = new List<double>();
            var costs = new List<double>();
            var rejected = 0;
            var errors = 0;
            var recent = new List<string>();
            Console.WriteLine($"== {model}{(reasoning ? " (reasoning on)" : "")}");
            foreach (var facts in Session)
            {
                var packet = new NarrationPacket("morning", "asphalt", new[] { "shovel", "chest" }, facts, recent.ToArray(), 0, 0);
                var started = Stopwatch.GetTimestamp();
                double? firstToken = null;
                var outcome = await narrator.NarrateAsync(packet, _ => firstToken ??= Stopwatch.GetElapsedTime(started).TotalSeconds);
                total.Add(Stopwatch.GetElapsedTime(started).TotalSeconds);
                if (firstToken is { } t) first.Add(t);
                costs.Add(outcome.Calls.Sum(c => c.CostUsd ?? 0));
                if (outcome.Status == NarrationStatus.Ok)
                {
                    recent.Add(outcome.Text!);
                    if (recent.Count > settings.NarrationRecent) recent.RemoveAt(0);
                }
                else if (outcome.Status == NarrationStatus.Rejected) rejected++;
                else errors++;
                Console.WriteLine($"  {outcome.Status,-9} {(firstToken is { } f ? f.ToString("0.00", CultureInfo.InvariantCulture) : "-"),5} s  {outcome.Text ?? outcome.Detail}");
            }
            var p95First = first.Count == 0 ? double.PositiveInfinity : Percentile(first, 0.95);
            var rate = 100.0 * rejected / Session.Length;
            Console.WriteLine($"  first token p50 {Percentile(first, 0.5):0.00} s, p95 {p95First:0.00} s; total p50 {Percentile(total, 0.5):0.00} s, p95 {Percentile(total, 0.95):0.00} s");
            Console.WriteLine($"  cost per narration {costs.Average():0.000000} USD; rejected after retry {rejected}/{Session.Length} = {rate:0.0} %; errors {errors}");
            var pass = errors == 0 && p95First < 2.5 && rate < 10;
            Console.WriteLine(pass ? "  PASS" : "  FAIL");
            anyPassed |= pass;
        }
        return anyPassed ? 0 : 1;
    }

    private static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0) return double.NaN;
        var sorted = values.Order().ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];
    }
}
