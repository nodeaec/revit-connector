using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
#if NET8_0_OR_GREATER
using System.Runtime.Loader;
#endif
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Windows;
using NodeAec.Connector.Client;
using NodeAec.Connector.Commands;
using NodeAec.Connector.Storage;
using NodeAec.Connector.UI;

namespace NodeAec.Connector;

/// <summary>
/// Plugin entry point for the Node.aec Connector for Autodesk Revit.
/// Sets up the canonical 'Node.aec' tab, the 'Conector' panel (large "Minha Conta"
/// button + small stacked "Meus Plugins" and "Explorar Catálogo" buttons), tab
/// deduplication via AdWindows, and background heartbeat.
/// </summary>
public class App : IExternalApplication
{
    public const string TabName = "Node.aec";
    public const string PanelName = "Conector";

    static App()
    {
#if NET8_0_OR_GREATER
        AssemblyLoadContext.Default.Resolving += (context, name) =>
            LoadFromAddInFolder(name);
#endif
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            LoadFromAddInFolder(new AssemblyName(args.Name));
    }

    private static Assembly? LoadFromAddInFolder(AssemblyName name)
    {
        try
        {
            string dir = Path.GetDirectoryName(typeof(App).Assembly.Location)
                ?? AppDomain.CurrentDomain.BaseDirectory;
            string candidate = Path.Combine(dir, $"{name.Name}.dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }
        catch
        {
            return null;
        }
    }

    public Result OnStartup(UIControlledApplication application)
    {
        // M3: top-level add-in guard. An exception escaping OnStartup makes Revit
        // report "failed to load the add-in" with a half-built ribbon and no recorded
        // cause; here the failure becomes a clean Result.Failed, with the exception type
        // (never the message content — it may contain paths/PII) in the local log.
        try
        {
            return Initialize(application);
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("ERROR", $"Falha ao montar a ribbon Node.aec: {ex.GetType().Name}.");
            return Result.Failed;
        }
    }

    /// <summary>
    /// Body of <see cref="OnStartup"/>: canonical tab, deduplication, "Conector" panel,
    /// buttons, AdWindows hooks, and heartbeat. Any unhandled exception propagates to the
    /// <c>OnStartup</c> top-level guard, which converts it into <c>Result.Failed</c>.
    /// </summary>
    private static Result Initialize(UIControlledApplication application)
    {
        // 1. Creates the canonical "Node.aec" tab if missing. Only the "name already in
        //    use"/invalid-name exception is treated as expected state (add-in reload); any
        //    other failure propagates to the top-level guard. An unfiltered catch — as before —
        //    masked any real error and the cause resurfaced later, untraced, as a
        //    CreateRibbonPanel ArgumentException.
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch (Exception ex) when (
            ex is Autodesk.Revit.Exceptions.ArgumentException || ex is ArgumentException)
        {
            Diagnostics.ConnectorLog.Write("INFO", $"Aba '{TabName}' já existia ou nome rejeitado: {ex.GetType().Name}.");
        }

        // 2. Cleans up stray elements and removes duplicate tabs
        CleanRogueRibbonElements();
        DeduplicateRibbonTabs(TabName);

        string assemblyPath = typeof(App).Assembly.Location;
        string addInDir = Path.GetDirectoryName(assemblyPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        // 3. Gets or creates the "Conector" governance panel
        Autodesk.Revit.UI.RibbonPanel connectorPanel = GetOrCreatePanel(application, TabName, PanelName);

        // 4. Primary (large) button: "Minha Conta"
        var btnManageData = new PushButtonData(
            "NodeAec_ManageConnector",
            "Minha\nConta",
            assemblyPath,
            typeof(ManageConnectorCommand).FullName ?? string.Empty)
        {
            ToolTip = "Gerencie sua conta Node.aec: entrar, sair e atualizar suas licenças."
        };
        LoadButtonIcons(btnManageData, addInDir);
        AddButtonIfMissing(connectorPanel, btnManageData);

        // 5-6. Secondary buttons (small, stacked): "Meus Plugins" + "Explorar Catálogo".
        // "Meus Plugins" stays disabled until login (RequiresLoginAvailability).
        var btnPluginsData = new PushButtonData(
            "NodeAec_ManagePlugins",
            "Meus\nPlugins",
            assemblyPath,
            typeof(ManagePluginsCommand).FullName ?? string.Empty)
        {
            ToolTip = "Veja os plugins vinculados à sua conta, com link para cada produto.",
            AvailabilityClassName = typeof(RequiresLoginAvailability).FullName ?? string.Empty
        };
        btnPluginsData.Image = UiTheme.PluginsIcon(large: false);
        btnPluginsData.LargeImage = UiTheme.PluginsIcon(large: true);

        var btnCatalogData = new PushButtonData(
            "NodeAec_ExploreCatalog",
            "Explorar\nCatálogo",
            assemblyPath,
            typeof(ExploreCatalogCommand).FullName ?? string.Empty)
        {
            ToolTip = "Explorar plugins, famílias e templates no marketplace Node.aec."
        };
        btnCatalogData.Image = UiTheme.CatalogIcon(large: false);
        btnCatalogData.LargeImage = UiTheme.CatalogIcon(large: true);
        AddStackedButtonsIfMissing(connectorPanel, btnPluginsData, btnCatalogData);

        // 7. Defensive Revit ribbon lifecycle hooks. Named static handlers with "-="
        // before "+=": if the add-in reloads in the same process (Add-In Manager),
        // delegates do not accumulate (L10).
        try
        {
            application.ControlledApplication.ApplicationInitialized -= OnApplicationInitialized;
            application.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;

            ComponentManager.UIElementActivated -= OnUiElementActivated;
            ComponentManager.UIElementActivated += OnUiElementActivated;
        }
        catch
        {
        }

        // 8. Periodic background heartbeat (non-blocking): fires immediately on startup and
        // renews the lease every 6 h — it used to be one-shot, so a machine that started offline
        // never renewed for the whole session. Failure/offline simply waits for the next
        // firing. The atomic swap avoids stacked timers if the add-in reloads in the
        // same process (the previous timer is discarded).
        var heartbeatTimer = new Timer(_ => RunHeartbeat(), null, TimeSpan.Zero, HeartbeatPeriod);
        Interlocked.Exchange(ref _heartbeatTimer, heartbeatTimer)?.Dispose();

        return Result.Succeeded;
    }

    /// <summary>Lease heartbeat cadence (4 firings/day — negligible traffic).</summary>
    private static readonly TimeSpan HeartbeatPeriod = TimeSpan.FromHours(6);

    private static Timer? _heartbeatTimer;

    /// <summary>
    /// Fires one background heartbeat round (fire-and-forget). Only reads local storage
    /// and talks to the API — no Revit API on the thread pool. Any exception becomes a
    /// WARN with the error type (never the raw message).
    /// </summary>
    private static void RunHeartbeat()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                string? token = LeaseStorage.LoadMasterLease();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    var client = new ConnectorApiClient();
                    var heartbeat = await client.ValidateHeartbeatAsync(token).ConfigureAwait(false);

                    // M7: the logging decision (fixed text on the key/cache branches — never
                    // a raw server message) lives in HeartbeatLog, covered by tests.
                    string? warning = Diagnostics.HeartbeatLog.WarningMessage(heartbeat);
                    if (warning != null)
                    {
                        Diagnostics.ConnectorLog.Write("WARN", warning);
                    }
                }
            }
            catch (Exception ex)
            {
                // Silent when offline — but leaves the cause traceable in the local log.
                Diagnostics.ConnectorLog.Write("WARN", $"Heartbeat de lease interrompido: {ex.GetType().Name}.");
            }
        });
    }

    /// <summary>Reaction to the <c>ApplicationInitialized</c> event: deduplicates the tab and removes ghost panels.</summary>
    private static void OnApplicationInitialized(object? sender, EventArgs e)
    {
        DeduplicateRibbonTabs(TabName);
        CleanRogueRibbonElements();
    }

    /// <summary>Reaction to any ribbon element activation: keeps the tab unique.</summary>
    private static void OnUiElementActivated(object? sender, EventArgs e)
    {
        DeduplicateRibbonTabs(TabName);
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        return Result.Succeeded;
    }

    private static void LoadButtonIcons(PushButtonData button, string addInDir)
    {
        try
        {
            string icon32Path = Path.Combine(addInDir, "Resources", "nodeaec-32.png");
            if (!File.Exists(icon32Path)) icon32Path = Path.Combine(addInDir, "nodeaec-32.png");

            if (File.Exists(icon32Path))
            {
                var bmp32 = new BitmapImage();
                bmp32.BeginInit();
                bmp32.UriSource = new Uri(icon32Path, UriKind.Absolute);
                bmp32.CacheOption = BitmapCacheOption.OnLoad;
                bmp32.EndInit();
                bmp32.Freeze();
                button.LargeImage = bmp32;
            }

            string icon16Path = Path.Combine(addInDir, "Resources", "nodeaec-16.png");
            if (!File.Exists(icon16Path)) icon16Path = Path.Combine(addInDir, "nodeaec-16.png");

            if (File.Exists(icon16Path))
            {
                var bmp16 = new BitmapImage();
                bmp16.BeginInit();
                bmp16.UriSource = new Uri(icon16Path, UriKind.Absolute);
                bmp16.CacheOption = BitmapCacheOption.OnLoad;
                bmp16.EndInit();
                bmp16.Freeze();
                button.Image = bmp16;
            }
        }
        catch
        {
        }
    }

    private static Autodesk.Revit.UI.RibbonPanel GetOrCreatePanel(UIControlledApplication app, string tab, string panelName)
    {
        try
        {
            List<Autodesk.Revit.UI.RibbonPanel> existing = app.GetRibbonPanels(tab);
            var found = existing.FirstOrDefault(p => string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
        }
        catch
        {
        }

        return app.CreateRibbonPanel(tab, panelName);
    }

    private static PushButton? AddButtonIfMissing(Autodesk.Revit.UI.RibbonPanel panel, PushButtonData buttonData)
    {
        try
        {
            var items = panel.GetItems().ToList();
            if (RibbonDecisions.ContainsName(items.Select(i => i.Name), buttonData.Name))
            {
                return items.FirstOrDefault(i => string.Equals(i.Name, buttonData.Name, StringComparison.OrdinalIgnoreCase)) as PushButton;
            }
            return panel.AddItem(buttonData) as PushButton;
        }
        catch
        {
            return null;
        }
    }

    private static void AddStackedButtonsIfMissing(Autodesk.Revit.UI.RibbonPanel panel, PushButtonData first, PushButtonData second)
    {
        try
        {
            var existing = panel.GetItems().Select(i => i.Name).ToList();

            // M7: the decision (stack / standalone / nothing) is pure and tested in RibbonDecisions;
            // only the UI commands remain here.
            switch (RibbonDecisions.PlanStackedInsertion(existing, first.Name, second.Name))
            {
                case StackedInsertion.None:
                    return;
                case StackedInsertion.StackBoth:
                    panel.AddStackedItems(first, second);
                    return;
                case StackedInsertion.AddFirstOnly:
                    AddButtonIfMissing(panel, first);
                    return;
                case StackedInsertion.AddSecondOnly:
                    AddButtonIfMissing(panel, second);
                    return;
            }
        }
        catch
        {
        }
    }

    public static void DeduplicateRibbonTabs(string targetTitle)
    {
        try
        {
            Autodesk.Windows.RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            var matchingTabs = ribbon.Tabs
                .Where(t => string.Equals(t.Title, targetTitle, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(t.Id, targetTitle, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingTabs.Count > 1)
            {
                var primaryTab = matchingTabs[0];

                for (int i = 1; i < matchingTabs.Count; i++)
                {
                    var duplicateTab = matchingTabs[i];

                    foreach (var panel in duplicateTab.Panels.ToList())
                    {
                        duplicateTab.Panels.Remove(panel);
                        bool alreadyInPrimary = primaryTab.Panels.Any(p =>
                            string.Equals(p.Source?.Title, panel.Source?.Title, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.Source?.Id, panel.Source?.Id, StringComparison.OrdinalIgnoreCase));

                        if (!alreadyInPrimary)
                        {
                            primaryTab.Panels.Add(panel);
                        }
                    }

                    duplicateTab.IsVisible = false;
                    ribbon.Tabs.Remove(duplicateTab);
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Removes legacy ribbon elements: the "License"/"Licensing" tabs and the
    /// "Conectar Conta" button (retired in favor of "Minha Conta" + "Meus Plugins").
    /// </summary>
    public static void CleanRogueRibbonElements()
    {
        try
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            var rogueTabs = ribbon.Tabs.Where(t =>
            {
                string title = t.Title?.Trim() ?? string.Empty;
                string id = t.Id?.Trim() ?? string.Empty;
                return string.Equals(title, "License", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(title, "Licensing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, "License", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, "Licensing", StringComparison.OrdinalIgnoreCase);
            }).ToList();

            foreach (var rogueTab in rogueTabs)
            {
                try
                {
                    rogueTab.IsVisible = false;
                    ribbon.Tabs.Remove(rogueTab);
                }
                catch { }
            }

            // Removes the legacy "Conectar Conta" button wherever it persists.
            foreach (var tab in ribbon.Tabs)
            {
                foreach (var panel in tab.Panels)
                {
                    try
                    {
                        // Captures the source once: `panel.Source` is nullable and dereferencing it
                        // inside the loop repeated the check (CS8602) and opened room for a race
                        // if the source were swapped between removals.
                        var items = panel.Source?.Items;
                        if (items == null) continue;

                        var legacyItems = items
                            .Where(item => string.Equals(item.Id, "NodeAec_LoginConnector", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        foreach (var legacy in legacyItems)
                        {
                            items.Remove(legacy);
                        }
                    }
                    catch { }
                }
            }
        }
        catch
        {
        }
    }
}
