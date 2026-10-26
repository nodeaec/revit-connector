using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Autodesk.Windows;

namespace NodeAec.Connector.UI;

/// <summary>
/// Keeps the panels on the <c>Node.aec</c> tab in order (<see cref="RibbonOrder"/>): the
/// Connector's panel first, then the user's own order, then new panels alphabetically. Watches
/// the tab's panel collection so a panel added later is placed, and a drag by the user is saved
/// (the Connector's panel goes back to first). Runs on Revit's UI thread; never throws.
/// </summary>
internal static class RibbonPanelOrdering
{
    private static RibbonTab? _tab;
    private static string _pinned = string.Empty;
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
    internal static void Attach(string tabTitle, string pinned)
    {
        try
        {
            var tab = ComponentManager.Ribbon?.Tabs.FirstOrDefault(t =>
                string.Equals(t.Title, tabTitle, StringComparison.OrdinalIgnoreCase)
                || string.Equals(t.Id, tabTitle, StringComparison.OrdinalIgnoreCase));
            if (tab == null || ReferenceEquals(tab, _tab))
            {
                return;
            }

            if (_tab != null)
            {
                _tab.Panels.CollectionChanged -= OnPanelsChanged;
            }

            _tab = tab;
            _pinned = pinned;
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
    /// Decides what a change was: a new panel (arrange it), the user dragging panels (save their
    /// order and put the pinned panel back first), or a panel leaving (just note it).
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
            if (!RibbonOrder.AllKnown(titles, Known))
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

    /// <summary>Moves the tab's panels into <see cref="RibbonOrder.Arrange"/>'s order.</summary>
    private static void Arrange()
    {
        if (_tab == null)
        {
            return;
        }

        var panels = _tab.Panels.ToList();
        var titles = panels.Select(Title).ToList();
        var order = RibbonOrder.Arrange(titles, _saved, _pinned);

        _applying = true;
        try
        {
            for (int target = 0; target < order.Count; target++)
            {
                int from = _tab.Panels.IndexOf(panels[order[target]]);
                if (from >= 0 && from != target)
                {
                    _tab.Panels.Move(from, target);
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

    private static List<string> Titles(RibbonTab tab) => tab.Panels.Select(Title).ToList();

    private static string Title(RibbonPanel panel) =>
        panel.Source?.Title?.Trim() is { Length: > 0 } title ? title : panel.Source?.Id ?? string.Empty;
}
