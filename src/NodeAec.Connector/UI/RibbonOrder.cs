using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.UI;

/// <summary>
/// Pure ordering rules for the <c>Node.aec</c> tab. Panels are company groups, by title: the
/// pinned panel (the Connector's) always comes first; the others follow the order the user last
/// dragged them into, and panels the user never placed come after those, alphabetically.
/// Inside a group, buttons are kept together per plugin (<see cref="ArrangeItems"/>).
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

    /// <summary>
    /// The plugin a button belongs to, from its internal name: the text before the first
    /// <c>_</c> (<c>Quant2_Exportar</c> → <c>Quant2</c>). Revit prefixes the ribbon id with
    /// <c>%</c>-separated tab and panel names, so only the last segment counts.
    /// </summary>
    /// <param name="itemId">Ribbon item id or internal name; may be empty (separators).</param>
    /// <returns>The group key; empty when the item has no name.</returns>
    internal static string ItemGroupKey(string? itemId)
    {
        string name = itemId ?? string.Empty;
        int lastSegment = name.LastIndexOf('%');
        if (lastSegment >= 0)
        {
            name = name.Substring(lastSegment + 1);
        }

        int separator = name.IndexOf('_');
        return separator > 0 ? name.Substring(0, separator) : name;
    }

    /// <summary>
    /// Orders the buttons of one panel shared by several plugins of a company: each plugin's
    /// buttons stay together and in their own order; the pinned plugin (the Connector) comes
    /// first, then plugins alphabetically by the text of their first button. An item without a
    /// name (a separator) stays with the button before it.
    /// </summary>
    /// <param name="keys">Group key of each item (<see cref="ItemGroupKey"/>), in panel order.</param>
    /// <param name="texts">Display text of each item, in panel order.</param>
    /// <param name="pinnedKey">Group that always comes first, or <c>null</c>.</param>
    /// <returns>Indices into <paramref name="keys"/>, in the order the items should appear.</returns>
    internal static IReadOnlyList<int> ArrangeItems(IReadOnlyList<string> keys, IReadOnlyList<string> texts, string? pinnedKey)
    {
        var effective = new string[keys.Count];
        string previous = string.Empty;
        for (int i = 0; i < keys.Count; i++)
        {
            effective[i] = string.IsNullOrEmpty(keys[i]) ? previous : keys[i];
            previous = effective[i];
        }

        var label = new Dictionary<string, string>(SameTitle);
        for (int i = 0; i < effective.Length; i++)
        {
            if (!label.ContainsKey(effective[i]) && !string.IsNullOrEmpty(keys[i]))
            {
                label[effective[i]] = texts[i];
            }
        }

        return Enumerable.Range(0, keys.Count)
            .OrderBy(i => pinnedKey != null && SameTitle.Equals(effective[i], pinnedKey) ? 0 : 1)
            .ThenBy(i => label.TryGetValue(effective[i], out var text) ? text : string.Empty, Alphabetical)
            .ThenBy(i => effective[i], SameTitle)
            .ToList();
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
