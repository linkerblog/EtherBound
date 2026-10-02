using System.Collections.Generic;
using System.Linq;

namespace EtherBound.Game.Ui;

/// <summary>
/// The `CARRY` panel's rows from the slots the sim reports (`left`, `right`, `both`, `back`): `L` and
/// `R`, or a single `HANDS` row for a two-handed object, then `BACK`. An empty slot reads `—`.
/// Asserted in `game.tests/CarryListTests.cs`.
/// </summary>
public static class CarryList
{
    public const string Empty = "—";

    public readonly record struct Item(string Slot, string Name, int Quantity);

    public readonly record struct Row(string Slot, string Text, bool Filled);

    public static IReadOnlyList<Row> Rows(IEnumerable<Item> carried)
    {
        var items = carried.ToList();
        Row Find(string label, params string[] slots)
        {
            var found = items.FirstOrDefault(item => slots.Contains(item.Slot));
            return found.Name is null ? new Row(label, Empty, false) : new Row(label, Text(found), true);
        }

        var rows = new List<Row>();
        var both = Find("HANDS", "both");
        if (both.Filled) rows.Add(both);
        else
        {
            rows.Add(Find("L", "left"));
            rows.Add(Find("R", "right"));
        }
        rows.Add(Find("BACK", "back"));
        // Anything in a slot this panel does not know still shows, so a new slot is never hidden.
        foreach (var item in items.Where(item => item.Slot is not ("left" or "right" or "both" or "back")))
            rows.Add(new Row(item.Slot.ToUpperInvariant(), Text(item), true));
        return rows;
    }

    private static string Text(Item item) =>
        item.Quantity > 1 ? $"{item.Name} ×{item.Quantity}" : item.Name;
}
