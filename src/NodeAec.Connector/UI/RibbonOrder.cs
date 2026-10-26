using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.UI;

/// <summary>
/// Pure ordering rules for the panels on the <c>Node.aec</c> tab, by panel title: the pinned
/// panel (the Connector's) always comes first; the others follow the order the user last
/// dragged them into, and panels the user never placed come after those, alphabetically.
/// No Revit/WPF types, so it runs in the headless test project;
/// <c>RibbonPanelOrdering</c> applies the result to the ribbon.
/// </summary>
internal static class RibbonOrder
{
    /// <summary>File under <c>%AppData%\NodeAec</c> that keeps the user's panel order.</summary>
    internal const string FileName = "ribbon-order.json";

    /// <summary>Alphabetical order as a Portuguese-speaking user reads it: case and accents folded.</summary>
    internal static readonly StringComparer Alphabetical =
        StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);

    private static readonly StringComparer SameTitle = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Computes where each current panel should go.
    /// </summary>
    /// <param name="current">Panel titles in their current order (duplicates allowed).</param>
    /// <param name="saved">The user's saved order, or <c>null</c> when they never reordered.</param>
    /// <param name="pinned">Title that always goes first, when present.</param>
    /// <returns>Indices into <paramref name="current"/>, in the order the panels should appear.</returns>
    internal static IReadOnlyList<int> Arrange(IReadOnlyList<string> current, IReadOnlyList<string>? saved, string pinned)
    {
        var remaining = Enumerable.Range(0, current.Count).ToList();
        var result = new List<int>(current.Count);

        TakeAll(remaining, result, i => SameTitle.Equals(current[i], pinned));

        foreach (string title in saved ?? Array.Empty<string>())
        {
            int match = remaining.FindIndex(i => SameTitle.Equals(current[i], title));
            if (match >= 0)
            {
                result.Add(remaining[match]);
                remaining.RemoveAt(match);
            }
        }

        // OrderBy is stable: panels with the same title keep their relative order.
        result.AddRange(remaining.OrderBy(i => current[i], Alphabetical));
        return result;
    }

    /// <summary>
    /// The order to save after the user drags panels: the current titles, without the pinned one
    /// and without repeats.
    /// </summary>
    /// <param name="current">Panel titles in their current order.</param>
    /// <param name="pinned">Title that is never saved, since it always goes first.</param>
    /// <returns>Titles to save.</returns>
    internal static IReadOnlyList<string> UserOrder(IEnumerable<string> current, string pinned)
    {
        var seen = new HashSet<string>(SameTitle) { pinned };
        return current.Where(t => !string.IsNullOrWhiteSpace(t) && seen.Add(t)).ToList();
    }

    /// <summary>Checks whether two title sequences are the same, ignoring case.</summary>
    internal static bool SameSequence(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Count == b.Count && a.Zip(b, (x, y) => SameTitle.Equals(x, y)).All(same => same);

    /// <summary>Checks whether every title in <paramref name="titles"/> is in <paramref name="known"/>.</summary>
    internal static bool AllKnown(IEnumerable<string> titles, IEnumerable<string> known)
    {
        var set = new HashSet<string>(known, SameTitle);
        return titles.All(set.Contains);
    }

    /// <summary>Path of the saved order file.</summary>
    internal static string FilePath() => Path.Combine(LeaseStorage.GetBaseDirectory(), FileName);

    /// <summary>Reads the saved order. Never throws.</summary>
    /// <returns>The saved titles, or <c>null</c> when there is no valid file.</returns>
    internal static IReadOnlyList<string>? Load()
    {
        try
        {
            string path = FilePath();
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("panels", out var panels) || panels.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return panels.EnumerateArray()
                .Where(p => p.ValueKind == JsonValueKind.String)
                .Select(p => p.GetString()!)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Saves the user's order (temp file + swap). Never throws.</summary>
    /// <param name="titles">Titles in the order to keep.</param>
    /// <returns><c>true</c> when the file was written.</returns>
    internal static bool Save(IReadOnlyList<string> titles)
    {
        try
        {
            string path = FilePath();
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Dictionary<string, IReadOnlyList<string>> { ["panels"] = titles }));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TakeAll(List<int> remaining, List<int> result, Predicate<int> match)
    {
        result.AddRange(remaining.Where(i => match(i)));
        remaining.RemoveAll(match);
    }
}
