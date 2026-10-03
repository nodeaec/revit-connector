using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Windows;
using NodeAec.Connector.Auth;
using NodeAec.Connector.Client;
using NodeAec.Connector.Config;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.UI;

/// <summary>
/// "Meus Plugins" window of the Node.aec Connector: lists the account-linked plugins,
/// each with a link to its product page. Requires login (the ribbon button stays
/// disabled until then, see <see cref="Commands.RequiresLoginAvailability"/>).
/// Visual identity follows the Node.aec web light mode.
/// </summary>
public class PluginsWindow : Window
{
    private readonly StackPanel _pluginsPanel;
    private readonly Button _btnLogin;
    private readonly Button _btnSync;
    private readonly TextBlock _txtFeedback;

    public PluginsWindow()
    {
        Title = "Meus Plugins — Node.aec";
        Width = 560;
        Height = 640;
        MinWidth = 520;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = UiTheme.Brush(UiTheme.Background);
        Foreground = UiTheme.Brush(UiTheme.Text);
        FontFamily = new FontFamily("Segoe UI, -apple-system, sans-serif");

        try
        {
            if (ComponentManager.ApplicationWindow != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = ComponentManager.ApplicationWindow;
            }
        }
        catch
        {
        }

        var mainScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(24)
        };

        var root = new StackPanel();
        mainScroll.Content = root;
        Content = mainScroll;

        // 1. Header
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock
        {
            Text = "Meus Plugins",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = UiTheme.Brush(UiTheme.Primary)
        });
        header.Children.Add(new TextBlock
        {
            Text = "Tudo o que a sua conta liberou para este computador.",
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 4, 0, 8)
        });
        header.Children.Add(new Border
        {
            Background = UiTheme.Brush(UiTheme.Accent),
            Height = 3,
            Width = 48,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(2)
        });
        root.Children.Add(header);

        // 2. Plugin list
        _pluginsPanel = new StackPanel();
        root.Children.Add(_pluginsPanel);

        // 3. Actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        _btnLogin = CreatePrimaryButton("Entrar com minha conta");
        _btnLogin.Click += async (s, e) => await WindowHandlerGuard.RunAsync(HandleBrowserLoginAsync, ReportHandlerError);
        actions.Children.Add(_btnLogin);

        _btnSync = CreateQuietButton("Atualizar lista");
        _btnSync.Margin = new Thickness(8, 0, 0, 0);
        _btnSync.Click += async (s, e) => await WindowHandlerGuard.RunAsync(HandleSyncAsync, ReportHandlerError);
        actions.Children.Add(_btnSync);
        root.Children.Add(actions);

        // 4. Feedback message
        _txtFeedback = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 12, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_txtFeedback);

        // 5. Footer
        root.Children.Add(BuildFooter());

        RefreshPlugins();
    }

    private FrameworkElement BuildFooter()
    {
        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var docInfo = new TextBlock
        {
            Text = $"Node.aec Connector {ConnectorConfig.Version}",
            FontSize = 11,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(docInfo, 0);
        footer.Children.Add(docInfo);

        var btnClose = CreateQuietButton("Fechar");
        btnClose.Click += (s, e) => Close();
        Grid.SetColumn(btnClose, 1);
        footer.Children.Add(btnClose);

        return footer;
    }

    private static Button CreatePrimaryButton(string content)
    {
        return new Button
        {
            Content = content,
            Background = UiTheme.Brush(UiTheme.Primary),
            Foreground = Brushes.White,
            Padding = new Thickness(16, 9, 16, 9),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FocusVisualStyle = null
        };
    }

    private static Button CreateQuietButton(string content)
    {
        return new Button
        {
            Content = content,
            Background = UiTheme.Brush(UiTheme.SoftBackground),
            Foreground = UiTheme.Brush(UiTheme.Text),
            Padding = new Thickness(14, 8, 14, 8),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FocusVisualStyle = null
        };
    }

    private static Button CreateLinkButton(string content)
    {
        return new Button
        {
            Content = content,
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.Primary),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = null
        };
    }

    /// <summary>
    /// Renders the plugin list from the local lease. Called from the constructor and from
    /// the async handlers' <c>finally</c> blocks, so it is <b>noexcept</b> by
    /// contract (H1): an exception here would escape through the WPF dispatcher and kill
    /// the Revit process.
    /// </summary>
    public void RefreshPlugins()
    {
        try
        {
            RenderPlugins();
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("WARN", $"Falha ao atualizar a lista de plugins: {ex.GetType().Name}.");
            try
            {
                _pluginsPanel.Children.Clear();
                _pluginsPanel.Children.Add(new TextBlock
                {
                    Text = "Não foi possível carregar a lista de plugins agora. Tente atualizar.",
                    FontSize = 13,
                    Foreground = UiTheme.Brush(UiTheme.Accent),
                    Margin = new Thickness(0, 4, 0, 4),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            catch
            {
                // Window possibly already disposed: nothing left to report from here.
            }
        }
    }

    /// <summary>Body of <see cref="RefreshPlugins"/> — split out so the shell is the only exception zone.</summary>
    private void RenderPlugins()
    {
        _pluginsPanel.Children.Clear();

        // M7: the (logged-out / empty / list) branch and the active-first ordering are
        // pure decisions in UiState, covered by headless tests.
        bool isLoggedIn = LoginRequirement.IsLoggedIn();
        IReadOnlyList<EntitlementItem> entitlements = Array.Empty<EntitlementItem>();
        if (isLoggedIn)
        {
            string? jwtToken = LeaseStorage.LoadMasterLease();
            var payload = string.IsNullOrWhiteSpace(jwtToken) ? null : LeaseStorage.ParseJwtPayload(jwtToken);
            entitlements = payload?.Entitlements ?? new List<EntitlementItem>();
        }

        switch (UiState.PluginsBranch(isLoggedIn, entitlements.Count))
        {
            case PluginsView.LoggedOut:
                _btnLogin.Visibility = Visibility.Visible;
                _btnSync.Visibility = Visibility.Collapsed;
                _pluginsPanel.Children.Add(new TextBlock
                {
                    Text = UiState.LoggedOutPluginsText,
                    FontSize = 13,
                    Foreground = UiTheme.Brush(UiTheme.TextSecondary),
                    Margin = new Thickness(0, 4, 0, 4),
                    TextWrapping = TextWrapping.Wrap
                });
                return;

            case PluginsView.Empty:
                _btnLogin.Visibility = Visibility.Collapsed;
                _btnSync.Visibility = Visibility.Visible;
                _pluginsPanel.Children.Add(new TextBlock
                {
                    Text = UiState.NoPluginsText,
                    FontSize = 13,
                    Foreground = UiTheme.Brush(UiTheme.TextSecondary),
                    Margin = new Thickness(0, 4, 0, 8),
                    TextWrapping = TextWrapping.Wrap
                });
                var btnCatalog = CreateLinkButton("Conhecer o catálogo de plugins ↗");
                btnCatalog.Click += (s, e) => OpenUrl(ConnectorConfig.CatalogUrl);
                _pluginsPanel.Children.Add(btnCatalog);
                return;

            default:
                _btnLogin.Visibility = Visibility.Collapsed;
                _btnSync.Visibility = Visibility.Visible;
                foreach (var ent in UiState.PluginsActiveFirst(entitlements))
                {
                    _pluginsPanel.Children.Add(BuildPluginCard(ent));
                }
                return;
        }
    }

    private FrameworkElement BuildPluginCard(EntitlementItem item)
    {
        var card = new Border
        {
            Background = UiTheme.Brush(UiTheme.Card),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var stack = new StackPanel();

        var nameBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.Name) ? item.Slug : item.Name,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = UiTheme.Brush(UiTheme.Text),
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(nameBlock);

        bool active = item.IsActive();
        // Defense: STJ may write null into "status" (NRT does not stop it) — never throw NRE.
        string statusUpper = string.IsNullOrWhiteSpace(item.Status) ? "INATIVO" : item.Status.ToUpperInvariant();
        string statusText = item.ExpiresAt.HasValue
            ? (active ? $"Liberado até {item.ExpiresAt.Value:dd/MM/yyyy}" : $"Expirado em {item.ExpiresAt.Value:dd/MM/yyyy}")
            : (active ? "Liberado — sem data para expirar" : statusUpper);
        stack.Children.Add(new TextBlock
        {
            Text = statusText,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(active ? UiTheme.Primary : UiTheme.Accent),
            Margin = new Thickness(0, 4, 0, 6)
        });

        string productUrl = ProductLinks.BuildProductUrl(item.Slug);
        var btnProduct = CreateLinkButton("Abrir página do produto ↗");
        btnProduct.HorizontalAlignment = HorizontalAlignment.Left;
        btnProduct.Padding = new Thickness(0);
        btnProduct.Click += (s, e) => OpenUrl(productUrl);
        stack.Children.Add(btnProduct);

        card.Child = stack;
        return card;
    }

    private async System.Threading.Tasks.Task HandleBrowserLoginAsync()
    {
        SetFeedback("Abrindo o navegador para você entrar com segurança...", UiTheme.Primary);
        _btnLogin.IsEnabled = false;

        try
        {
            var authService = new DesktopAuthService();
            string userToken = await authService.LoginViaBrowserAsync().ConfigureAwait(true);

            var client = new ConnectorApiClient();
            var syncResult = await client.SyncMasterEntitlementsAsync(userToken).ConfigureAwait(true);

            if (syncResult.Success)
            {
                // Displayed identity comes from the session token claims (the master lease carries no identity).
                var userClaims = LeaseStorage.ParseUserSessionClaims(userToken);

                if (!LeaseStorage.SaveSession(userClaims?.Email, userToken, userClaims?.Name))
                {
                    SetFeedback("Plugins recebidos, mas não foi possível salvar a sessão localmente. Verifique as permissões do usuário.", UiTheme.Accent);
                }
                else
                {
                    // M1: key-degradation warning takes priority over success.
                    string? keysWarning = syncResult.VerificationWarning;
                    SetFeedback(
                        keysWarning ?? $"Bem-vindo! {syncResult.GrantedCount} plugin(s) liberado(s).",
                        keysWarning == null ? UiTheme.Primary : UiTheme.Accent);
                }
            }
            else
            {
                SetFeedback($"Não foi possível buscar seus plugins: {syncResult.Message}", UiTheme.Accent);
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Não foi possível concluir o login: {ex.Message}", UiTheme.Accent);
        }
        finally
        {
            _btnLogin.IsEnabled = true;
            RefreshPlugins();
        }
    }

    private async System.Threading.Tasks.Task HandleSyncAsync()
    {
        SetFeedback("Atualizando sua lista...", UiTheme.Primary);
        _btnSync.IsEnabled = false;

        try
        {
            var session = LeaseStorage.LoadSession();
            var client = new ConnectorApiClient();

            var result = session.HasValue && !string.IsNullOrWhiteSpace(session.Value.Token)
                ? await client.SyncMasterEntitlementsAsync(session.Value.Token).ConfigureAwait(true)
                : await client.ValidateHeartbeatAsync().ConfigureAwait(true);

            // M1: surfaces the verification-key state in the sync/heartbeat result.
            string? keysWarning = result.Success ? result.VerificationWarning : null;
            SetFeedback(
                result.Success
                    ? keysWarning ?? "Lista atualizada."
                    : $"Não foi possível atualizar agora: {result.Message}",
                result.Success && keysWarning == null ? UiTheme.Primary : UiTheme.Accent);
        }
        catch (Exception ex)
        {
            SetFeedback($"Sem conexão no momento: {ex.Message}", UiTheme.Accent);
        }
        finally
        {
            _btnSync.IsEnabled = true;
            RefreshPlugins();
        }
    }

    private void SetFeedback(string message, Color color)
    {
        _txtFeedback.Text = message;
        _txtFeedback.Foreground = UiTheme.Brush(color);
    }

    /// <summary>
    /// Reports to the user a handler failure that would have taken the dispatcher down — fixed,
    /// sanitized text (type name, never message content). Never throws; used
    /// as the <see cref="WindowHandlerGuard"/> callback.
    /// </summary>
    private void ReportHandlerError(Exception ex)
    {
        SetFeedback(
            $"Ocorreu um erro inesperado ({ex.GetType().Name}). Tente novamente; se persistir, reinicie o Revit.",
            UiTheme.Accent);
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // N3: browser failure never passes silently — feedback in the window itself.
            SetFeedback($"Não foi possível abrir o link ({ex.GetType().Name}).", UiTheme.Accent);
        }
    }

    private static PluginsWindow? _instance;

    /// <summary>
    /// Opens (or reactivates) the single plugins window. Repeated ribbon clicks do not
    /// stack windows — each one syncs and writes storage in parallel (L11).
    /// </summary>
    public static void Open()
    {
        if (_instance != null)
        {
            _instance.Activate();
            return;
        }

        _instance = new PluginsWindow();
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
        _instance.Activate();
    }
}
