// Free-text interpretation bench (Dev-007 [Sec. 4]). Needs a live Jev key unless run with --list.
//
//   dotnet run --project sim/EtherBound.Llm.Bench -- --list      print the candidates the corpus runs against
//   dotnet run --project sim/EtherBound.Llm.Bench                run the corpus against Jev and print the verdict
//   dotnet run --project sim/EtherBound.Llm.Bench -- narrator    the narrator bench of Dev-008 (see NarratorBench.cs)
//
// The pass rule is written before the numbers: adopt accept = 0.75 only if at least 85 % of the sentences
// with a matching candidate are submitted correctly and fewer than 5 % of the sentences with none are
// submitted at all.

using System.Diagnostics;
using System.Globalization;
using EtherBound.Llm;
using EtherBound.Llm.Bench;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;

if (args.Contains("narrator")) return await NarratorBench.Run(args);

var listOnly = args.Contains("--list");
var configDirectory = args.SkipWhile(a => a != "--config").Skip(1).FirstOrDefault();

using var engine = new WorldEngine();
engine.NewGame(7, "test");
BenchWorld.StandBesideTheObjects(engine);
var player = engine.GetState().Actors.First(a => a.Id == Ids.Player);
var settings = LlmSettings.Load();
var candidates = CandidateBuilder.Build(engine.Menu(Ids.Player, player.X, player.Y, player.Z, 1), settings.MaxCandidates);

Console.WriteLine($"{candidates.Length} candidates at ({player.X:0.0}, {player.Y:0.0}):");
foreach (var candidate in candidates)
    Console.WriteLine($"  {candidate.Key} {candidate.Label}{(candidate.Available ? "" : "   [refused: " + candidate.Reason + "]")}");

var corpus = Corpus.All;
var missing = corpus.Where(c => c.Expected is not null && !candidates.Any(k => k.Label == c.Expected)).ToList();
if (missing.Count > 0)
{
    foreach (var c in missing) Console.Error.WriteLine($"corpus expects a label that is not a candidate: {c.Expected}");
    return 3;
}
Console.WriteLine($"corpus: {corpus.Count} sentences, every expected label is a candidate");
if (listOnly) return 0;

using var runtime = LlmRuntime.Create(configDirectory);
if (!runtime.Interpreter.Configured)
{
    Console.Error.WriteLine($"No Jev key: set {LlmConfig.TypeSafeKeyVariable} or put typesafe_key in {LlmConfig.FileName} (--config DIR).");
    return 2;
}

var rows = new List<(Corpus.Line Line, Interpretation Result, double Seconds)>();
foreach (var line in corpus)
{
    var started = Stopwatch.GetTimestamp();
    var result = await runtime.Interpreter.InterpretAsync(line.Text, candidates);
    rows.Add((line, result, Stopwatch.GetElapsedTime(started).TotalSeconds));
    Console.WriteLine($"  {result.Kind,-11} {result.Confidence,4:0.00} {(result.Candidate?.Label ?? "-"),-48} <- {line.Text}");
}

var matching = rows.Where(r => r.Line.Expected is not null).ToList();
var unmatched = rows.Where(r => r.Line.Expected is null).ToList();
var correct = matching.Count(r => r.Result.Kind == InterpretKind.Accept && r.Result.Candidate!.Label == r.Line.Expected);
var asked = matching.Count(r => r.Result.Kind == InterpretKind.Confirm && r.Result.Candidate!.Label == r.Line.Expected);
var wrong = matching.Count(r => r.Result.Kind == InterpretKind.Accept && r.Result.Candidate!.Label != r.Line.Expected);
var submittedAnyway = unmatched.Count(r => r.Result.Kind == InterpretKind.Accept);
var unavailable = rows.Count(r => r.Result.Kind == InterpretKind.Unavailable);
var seconds = rows.Select(r => r.Seconds).Order().ToList();

double Percentile(double p) => seconds[Math.Min(seconds.Count - 1, (int)Math.Ceiling(p * seconds.Count) - 1)];
string Pct(int n, int of) => of == 0 ? "n/a" : (100.0 * n / of).ToString("0.0", CultureInfo.InvariantCulture) + " %";

Console.WriteLine();
Console.WriteLine($"sentences: {rows.Count} ({matching.Count} with a matching candidate, {unmatched.Count} without), unavailable: {unavailable}");
Console.WriteLine($"submitted correctly: {correct}/{matching.Count} = {Pct(correct, matching.Count)}   asked instead: {asked}   submitted wrongly: {wrong}");
Console.WriteLine($"no match, submitted anyway: {submittedAnyway}/{unmatched.Count} = {Pct(submittedAnyway, unmatched.Count)}");
Console.WriteLine($"latency p50 {Percentile(0.5):0.00} s, p95 {Percentile(0.95):0.00} s");
Console.WriteLine($"cost per call {rows.Average(r => r.Result.CostUsd):0.000000} USD, input tokens per call {rows.Average(r => r.Result.TokensIn):0}");
var pass = unavailable == 0 && matching.Count > 0 && correct >= 0.85 * matching.Count && submittedAnyway < 0.05 * unmatched.Count;
Console.WriteLine(pass ? "PASS: adopt accept = 0.75" : "FAIL: raise the threshold, fix the labels, or fall back to the OpenRouter interpreter");
return pass ? 0 : 1;
