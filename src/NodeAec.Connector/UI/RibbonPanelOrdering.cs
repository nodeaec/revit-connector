using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Autodesk.Windows;

namespace NodeAec.Connector.UI;

/// <summary>
/// Keeps the <c>Node.aec</c> tab tidy (<see cref="RibbonOrder"/>): panels with the same title
/// (one company's plugins) become one group; the Connector's panel comes first, then the user's
/// own order, then new panels alphabetically; inside a group each plugin's buttons stay together,
/// alphabetically. Watches the tab's panel collection so a panel added later is placed, and a drag
/// by the user is saved (the Connector's panel goes back to first). Runs on Revit's UI thread;
/// never throws.
/// </summary>
internal static class RibbonPanelOrdering
{
    private static RibbonTab? _tab;
    private static string _pinned = string.Empty;
    private static string _pinnedItemKey = string.Empty;
    private static IReadOnlyList<string>? _saved;
    private static bool _savedLoaded;
    private static bool _applying;
    private static bool _pending;

    /// <summary>Order the tab had after the Connector last placed or saw it.</summary>
    private static List<string> _lastOrder = new();

    /// <summary>Every title seen on the tab this session; a title outside it is a new panel.</summary>
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Finds the tab, starts watching it if it changed, and arranges it. Cheap when already
    /// attached, so it can run on every ribbon activation.
    /// </summary>
    /// <param name="tabTitle">Title of the tab (<c>Node.aec</c>).</param>
    /// <param name="pinned">Panel title that always goes first.</param>
    /// <param name="pinnedItemId">Internal name of a button whose plugin goes first inside the pinned panel.</param>
    internal static void Attach(string tabTitle, string pinned, string pinnedItemId)
    {
        try
        {
            var tab = ComponentManager.Ribbon?.Tabs.FirstOrDefault(t =>
                string.Equals(t.Title, tabTitle, StringComparison.OrdinalIgnoreCase)
                || string.Equals(t.Id, tabTitle, StringComparison.OrdinalIgnoreCase));
            if (tab == null)
            {
                return;
            }

            if (ReferenceEquals(tab, _tab))
            {
                ApplyPinnedStyles();
                return;
            }

            if (_tab != null)
            {
                _tab.Panels.CollectionChanged -= OnPanelsChanged;
            }

            _tab = tab;
            _pinned = pinned;
            _pinnedItemKey = RibbonOrder.ItemGroupKey(pinnedItemId);
            if (!_savedLoaded)
            {
                _saved = RibbonOrder.Load();
                _savedLoaded = true;
            }

            Known.Clear();
            Arrange();
            tab.Panels.CollectionChanged += OnPanelsChanged;
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("WARN", $"Não foi possível ordenar os painéis da aba: {ex.GetType().Name}.");
        }
    }

    private static void OnPanelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_applying || _pending)
        {
            return;
        }

        // A drag can arrive as Remove + Add; wait until the ribbon settles before deciding.
        _pending = true;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Reconcile));
    }

    /// <summary>
    /// Decides what a change was: a new panel (merge and arrange it), the user dragging panels
    /// (save their order and put the pinned panel back first), or a panel leaving (just note it).
    /// </summary>
    private static void Reconcile()
    {
        _pending = false;
        try
        {
            if (_tab == null)
            {
                return;
            }

            var titles = Titles(_tab);
            if (!RibbonOrder.AllKnown(titles, Known) || titles.Distinct(StringComparer.OrdinalIgnoreCase).Count() < titles.Count)
            {
                Arrange();
                return;
            }

            if (RibbonOrder.SameSequence(titles, _lastOrder))
            {
                return;
            }

            if (titles.Count < _lastOrder.Count)
            {
                _lastOrder = titles;
                return;
            }

            var userOrder = RibbonOrder.UserOrder(titles, _pinned);
            _saved = userOrder;
            _ = Task.Run(() => RibbonOrder.Save(userOrder));
            Arrange();
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("WARN", $"Não foi possível ordenar os painéis da aba: {ex.GetType().Name}.");
        }
    }

    /// <summary>
    /// Merges same-title panels, moves the panels into <see cref="RibbonOrder.Arrange"/>'s order,
    /// and orders the buttons inside each panel.
    /// </summary>
    private static void Arrange()
    {
        if (_tab == null)
        {
            return;
        }

        _applying = true;
        try
        {
            MergeSameTitle(_tab);

            var panels = _tab.Panels.ToList();
            var order = RibbonOrder.Arrange(panels.Select(Title).ToList(), _saved, _pinned);
            for (int target = 0; target < order.Count; target++)
            {
                int from = _tab.Panels.IndexOf(panels[order[target]]);
                if (from >= 0 && from != target)
                {
                    _tab.Panels.Move(from, target);
                }
            }

            foreach (var panel in _tab.Panels)
            {
                bool isPinned = RibbonDecisions.IsNodeAecPanel(Title(panel), _pinned);
                ArrangeItems(panel, isPinned ? _pinnedItemKey : null);
                if (isPinned)
                {
                    ApplyPinnedPanelTitleBackground(panel);
                }
            }
        }
        finally
        {
            _applying = false;
        }

        _lastOrder = Titles(_tab);
        Known.UnionWith(_lastOrder);
    }

    /// <summary>
    /// Applies the Node.aec branded tint background (<see cref="UiTheme.RibbonPanelTitleBackground"/>)
    /// to the pinned panel's title bar using <see cref="RibbonPanel.CustomPanelTitleBarBackground"/>.
    /// </summary>
    private static void ApplyPinnedPanelTitleBackground(RibbonPanel panel)
    {
        try
        {
            panel.CustomPanelTitleBarBackground = UiTheme.Brush(UiTheme.RibbonPanelTitleBackground);
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("WARN", $"Não foi possível aplicar o estilo do painel Node.aec: {ex.GetType().Name}.");
        }
    }

    /// <summary>
    /// Refreshes the branded title background on any pinned Node.aec panels currently present on the tab.
    /// </summary>
    private static void ApplyPinnedStyles()
    {
        if (_tab == null)
        {
            return;
        }

        foreach (var panel in _tab.Panels)
        {
            if (RibbonDecisions.IsNodeAecPanel(Title(panel), _pinned))
            {
                ApplyPinnedPanelTitleBackground(panel);
            }
        }
    }

    /// <summary>
    /// One group per title: the buttons of every later panel with the same title move into the
    /// first one, and the emptied panel leaves the tab. A button whose id is already in the group
    /// is a leftover of an add-in reload and is dropped.
    /// </summary>
    private static void MergeSameTitle(RibbonTab tab)
    {
        var groups = new Dictionary<string, RibbonPanel>(StringComparer.OrdinalIgnoreCase);
        foreach (var panel in tab.Panels.ToList())
        {
            string title = Title(panel);
            if (!groups.TryGetValue(title, out var group))
            {
                groups[title] = panel;
                continue;
            }

            if (MoveItems(panel, group))
            {
                tab.Panels.Remove(panel);
            }
        }
    }

    /// <summary>Moves a panel's buttons into another; slide-out buttons stay in the slide-out.</summary>
    /// <returns><c>true</c> when the buttons were moved.</returns>
    private static bool MoveItems(RibbonPanel from, RibbonPanel to)
    {
        var source = from.Source?.Items;
        var target = to.Source?.Items;
        if (source == null || target == null)
        {
            return false;
        }

        var ids = new HashSet<string>(target.Select(i => i.Id).Where(id => !string.IsNullOrEmpty(id)), StringComparer.OrdinalIgnoreCase);
        int slideOutAt = IndexOfSlideOut(target);
        bool inSlideOut = false;
        foreach (var item in source.ToList())
        {
            source.Remove(item);
            if (item is RibbonPanelBreak)
            {
                inSlideOut = true;
                if (slideOutAt < 0)
                {
                    target.Add(item);
                    slideOutAt = target.Count - 1;
                }

                continue;
            }

            if (!string.IsNullOrEmpty(item.Id) && !ids.Add(item.Id))
            {
                continue;
            }

            if (!inSlideOut && slideOutAt >= 0)
            {
                target.Insert(slideOutAt++, item);
            }
            else
            {
                target.Add(item);
            }
        }

        return true;
    }

    /// <summary>Orders a panel's main buttons by plugin (<see cref="RibbonOrder.ArrangeItems"/>); the slide-out is left alone.</summary>
    private static void ArrangeItems(RibbonPanel panel, string? pinnedKey)
    {
        var items = panel.Source?.Items;
        if (items == null)
        {
            return;
        }

        int end = IndexOfSlideOut(items);
        var main = items.Take(end < 0 ? items.Count : end).ToList();
        if (main.Count < 2)
        {
            return;
        }

        var order = RibbonOrder.ArrangeItems(
            main.Select(i => RibbonOrder.ItemGroupKey(ItemId(i))).ToList(),
            main.Select(i => (i.Text ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim()).ToList(),
            pinnedKey);
        for (int target = 0; target < order.Count; target++)
        {
            int from = items.IndexOf(main[order[target]]);
            if (from >= 0 && from != target)
            {
                items.Move(from, target);
            }
        }
    }

    /// <summary>A button's id; for a stack of small buttons, the id of its first button.</summary>
    private static string ItemId(RibbonItem item)
    {
        if (item is RibbonRowPanel row)
        {
            var first = row.Items.FirstOrDefault(i => !string.IsNullOrEmpty(i.Id) && i is not RibbonRowBreak);
            if (first != null)
            {
                return ItemId(first);
            }
        }

        return item.Id ?? string.Empty;
    }

    private static int IndexOfSlideOut(RibbonItemCollection items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is RibbonPanelBreak)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> Titles(RibbonTab tab) => tab.Panels.Select(Title).ToList();

    private static string Title(RibbonPanel panel) =>
        panel.Source?.Title?.Trim() is { Length: > 0 } title ? title : panel.Source?.Id ?? string.Empty;
}
