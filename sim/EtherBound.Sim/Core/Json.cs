using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EtherBound.Sim.Core;

/// <summary>
/// JSON helpers. Payloads are built in Pydantic's field order; numbers that Python holds as floats
/// stay doubles, so a dump reads back to the same values in either runtime.
/// </summary>
public static class Json
{
    public static JsonObject Obj(params (string Key, JsonNode? Value)[] fields)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in fields) obj[key] = value;
        return obj;
    }

    public static JsonNode? Of(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        _ => throw new ArgumentException($"unsupported JSON value {value.GetType().Name}"),
    };

    public static JsonObject Parse(string text) => JsonNode.Parse(text)!.AsObject();

    public static string Dump(JsonNode? node) => node?.ToJsonString() ?? "null";

    /// <summary>Semantic equality: key order ignored, numbers compared as doubles (Python 1 == 1.0).</summary>
    public static bool Same(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        switch (a)
        {
            case JsonObject oa when b is JsonObject ob:
                return oa.Count == ob.Count && oa.All(p => ob.TryGetPropertyValue(p.Key, out var v) && Same(p.Value, v));
            case JsonArray aa when b is JsonArray ab:
                return aa.Count == ab.Count && aa.Zip(ab).All(p => Same(p.First, p.Second));
            case JsonValue va when b is JsonValue vb:
                var ka = va.GetValueKind();
                var kb = vb.GetValueKind();
                if (ka == JsonValueKind.Number && kb == JsonValueKind.Number) return ToDouble(va) == ToDouble(vb);
                if (ka is JsonValueKind.True or JsonValueKind.False && kb is JsonValueKind.True or JsonValueKind.False) return ka == kb;
                return ka == kb && ka == JsonValueKind.String && va.GetValue<string>() == vb.GetValue<string>();
            default:
                return false;
        }
    }

    public static string Str(this JsonNode? node, string key) => node![key]!.GetValue<string>();

    public static int Int(this JsonNode? node, string key) => ToInt(node![key]!);

    public static double Num(this JsonNode? node, string key) => ToDouble(node![key]!);

    /// <summary>Any JSON number as a double, whether parsed text or a value built in memory.</summary>
    public static double ToDouble(JsonNode node)
    {
        var value = node.AsValue();
        if (value.TryGetValue<double>(out var d)) return d;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<float>(out var f)) return f;
        throw new ArgumentException($"expected a number, got {value.ToJsonString()}");
    }

    /// <summary>An integer field as Pydantic's lax mode accepts it: an int, or a float with no fraction.</summary>
    public static int ToInt(JsonNode node)
    {
        var value = ToDouble(node);
        if (value != Math.Floor(value)) throw new ArgumentException($"expected an integer, got {value.ToString(CultureInfo.InvariantCulture)}");
        return (int)value;
    }
}
