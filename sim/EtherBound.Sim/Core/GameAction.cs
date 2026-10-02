using System.Text.Json.Nodes;

namespace EtherBound.Sim.Core;

/// <summary>
/// One submitted action: the discriminated union of <c>engine/actions.py</c> as a single record
/// with a per-op schema. <see cref="Parse"/> validates like Pydantic and <see cref="ToJson"/>
/// dumps the same fields in the same order.
/// </summary>
public sealed record GameAction
{
    private sealed record Schema(string[] Targets, string[]? Into = null, bool FloatVector = false, bool IntVector = false,
        bool Tool = false, bool DefaultSelf = false);

    private static readonly Dictionary<string, Schema> Schemas = new()
    {
        ["move"] = new(Array.Empty<string>(), FloatVector: true),
        ["inspect"] = new(new[] { "tile", "object", "actor" }),
        ["wait"] = new(new[] { "self" }, DefaultSelf: true),
        ["sleep"] = new(new[] { "self" }, DefaultSelf: true),
        ["eat"] = new(new[] { "object" }),
        ["drink"] = new(new[] { "tile", "object" }),
        ["dig"] = new(new[] { "tile" }),
        ["build"] = new(new[] { "tile", "edge" }),
        ["climb"] = new(new[] { "tile" }),
        ["take"] = new(new[] { "object" }),
        ["drop"] = new(new[] { "object" }),
        ["put"] = new(new[] { "object" }, Into: new[] { "object", "tile" }),
        ["open"] = new(new[] { "object" }),
        ["close"] = new(new[] { "object" }),
        ["wear"] = new(new[] { "object" }),
        ["remove"] = new(new[] { "object" }),
        ["push"] = new(new[] { "object", "actor" }, IntVector: true),
        ["pull"] = new(new[] { "object", "actor" }, IntVector: true),
        ["drag"] = new(new[] { "object", "actor" }, IntVector: true),
        ["throw"] = new(new[] { "object" }, IntVector: true),
        ["hit"] = new(new[] { "object", "actor", "edge" }, Tool: true),
        ["break"] = new(new[] { "object", "edge" }, Tool: true),
    };

    public required string Op { get; init; }
    public Target? Target { get; init; }
    public Target? Into { get; init; }
    public ObjectTarget? Tool { get; init; }
    public double Dx { get; init; }
    public double Dy { get; init; }

    public int IntDx => (int)Dx;
    public int IntDy => (int)Dy;

    public static IReadOnlyCollection<string> Ops => Schemas.Keys;

    public static GameAction Move(double dx, double dy) => Checked(new GameAction { Op = "move", Dx = dx, Dy = dy });
    public static GameAction Wait() => new() { Op = "wait", Target = new SelfTarget() };
    public static GameAction On(string op, Target target) => Checked(new GameAction { Op = op, Target = target });
    public static GameAction Put(ObjectTarget target, Target into) => Checked(new GameAction { Op = "put", Target = target, Into = into });
    public static GameAction Vector(string op, Target target, int dx, int dy) => Checked(new GameAction { Op = op, Target = target, Dx = dx, Dy = dy });
    public static GameAction Strike(string op, Target target, ObjectTarget? tool) => Checked(new GameAction { Op = op, Target = target, Tool = tool });

    private static GameAction Checked(GameAction action)
    {
        var schema = Schemas.GetValueOrDefault(action.Op) ?? throw new ArgumentException($"unknown op {action.Op}");
        if (schema.Targets.Length > 0 && (action.Target is null || !schema.Targets.Contains(action.Target.Kind)))
            throw new ArgumentException($"{action.Op}: target must be one of {string.Join(", ", schema.Targets)}");
        if (schema.Into is not null && (action.Into is null || !schema.Into.Contains(action.Into.Kind)))
            throw new ArgumentException($"{action.Op}: into must be one of {string.Join(", ", schema.Into)}");
        if ((schema.FloatVector || schema.IntVector) && (action.Dx is < -1 or > 1 || action.Dy is < -1 or > 1))
            throw new ArgumentException($"{action.Op}: dx and dy must be within [-1, 1]");
        if (schema.IntVector && (action.Dx != Math.Floor(action.Dx) || action.Dy != Math.Floor(action.Dy)))
            throw new ArgumentException($"{action.Op}: dx and dy must be integers");
        return action;
    }

    public static GameAction Parse(JsonNode node)
    {
        var op = node.Str("op");
        var schema = Schemas.GetValueOrDefault(op) ?? throw new ArgumentException($"unknown op {op}");
        var action = new GameAction
        {
            Op = op,
            Target = node["target"] is { } target ? Core.Target.Parse(target) : schema.DefaultSelf ? new SelfTarget() : null,
            Into = node["into"] is { } into ? Core.Target.Parse(into) : null,
            Tool = node["tool"] is { } tool ? (ObjectTarget)Core.Target.Parse(tool) : null,
            Dx = schema.FloatVector || schema.IntVector ? node.Num("dx") : 0,
            Dy = schema.FloatVector || schema.IntVector ? node.Num("dy") : 0,
        };
        return Checked(action);
    }

    public JsonObject ToJson()
    {
        var schema = Schemas[Op];
        var json = new JsonObject { ["op"] = Op };
        if (schema.FloatVector)
        {
            json["dx"] = Dx;
            json["dy"] = Dy;
            return json;
        }
        json["target"] = Target!.ToJson();
        if (schema.Into is not null) json["into"] = Into!.ToJson();
        if (schema.IntVector)
        {
            json["dx"] = IntDx;
            json["dy"] = IntDy;
        }
        if (schema.Tool) json["tool"] = Tool?.ToJson();
        return json;
    }
}
