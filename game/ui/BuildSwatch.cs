using System;
using System.Collections.Generic;

namespace EtherBound.Game.Ui;

/// <summary>
/// Finds the material a build slot's subject names. The sim's subject is "<Material name> wall" or
/// "<Material name> floor"; the longest catalog name that starts the subject wins, so `Dark soil`
/// is never mistaken for `Soil`. Asserted in `game.tests/BuildSwatchTests.cs`.
/// </summary>
public static class BuildSwatch
{
    /// <summary>The catalog colour (a `#rrggbb` string) for a subject, or null when none matches.</summary>
    public static string? ColorOf(string subject, IEnumerable<(string Name, string Color)> materials)
    {
        string? best = null;
        var bestLength = 0;
        foreach (var (name, color) in materials)
        {
            if (name.Length <= bestLength || string.IsNullOrWhiteSpace(color)) continue;
            if (!subject.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            // Match whole words: `Stone` must not claim `Stonework wall`.
            if (subject.Length > name.Length && subject[name.Length] != ' ') continue;
            best = color;
            bestLength = name.Length;
        }
        return best;
    }
}
