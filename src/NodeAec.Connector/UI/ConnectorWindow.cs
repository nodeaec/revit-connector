using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using NodeAec.Connector.Auth;
using NodeAec.Connector.Client;
using NodeAec.Connector.Config;
using NodeAec.Connector.Cryptography;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;
using NodeAec.Connector.Updates;

namespace NodeAec.Connector.UI;

/// <summary>
/// "Minha Conta" central governance window of the Node.aec Connector for Autodesk Revit.
/// Unifies account management, plugin inventory, computer licensing status, manual key activation,
/// and update verification in a single window.
/// Visual identity follows the Node.aec web light mode.
/// </summary>
public class ConnectorWindow : Window
{
    private readonly ScrollViewer _mainScroll;
    private readonly TextBlock _txtHeaderBadge;
    private readonly TextBlock _txtAccountTitle;
    private readonly TextBlock _txtAccountHint;
    private readonly Button _btnLogin;
    private readonly Button _btnLogout;
    private readonly StackPanel _pluginsPanel;
    private readonly TextBlock _txtLicenseStatus;
    private readonly Button _btnSync;
    private readonly TextBox _txtManualKey;
    private readonly Button _btnActivateKey;
    private readonly TextBlock _txtMachineId;
    private readonly FrameworkElement _updatesCard;
    private readonly TextBlock _txtInstalledVersion;
    private readonly TextBlock _txtUpdateStatus;
    private readonly TextBlock _txtUpdateNotes;
    private readonly Button _btnCheckUpdates;
    private readonly Button _btnDownloadInstall;
    private readonly TextBlock _txtFeedback;

    private LatestReleaseInfo? _latestRelease;

    public ConnectorWindow()
    {
        Title = "Minha Conta — Node.aec";
        Width = 580;
        Height = 740;
        MinWidth = 540;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = UiTheme.Brush(UiTheme.Background);
        Foreground = UiTheme.Brush(UiTheme.Text);
        FontFamily = new FontFamily("Segoe UI, -apple-system, sans-serif");

        try
        {
            var icon = LoadAppIcon();
            if (icon != null) Icon = icon;

            if (ComponentManager.ApplicationWindow != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = ComponentManager.ApplicationWindow;
            }
        }
        catch
        {
        }

        _mainScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(24)
        };

        var root = new StackPanel();
        _mainScroll.Content = root;
        Content = _mainScroll;

        // 1. Header
        root.Children.Add(BuildHeader(out _txtHeaderBadge));

        // 2. Sua conta card
        var accountCard = BuildCard("Sua conta", out var accountContent);
        _txtAccountTitle = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Text),
            TextWrapping = TextWrapping.Wrap
        };
        accountContent.Children.Add(_txtAccountTitle);

        _txtAccountHint = new TextBlock
        {
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 4, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        accountContent.Children.Add(_txtAccountHint);

        var accountButtons = new StackPanel { Orientation = Orientation.Horizontal };
        _btnLogin = CreatePrimaryButton("Entrar com minha conta");
        _btnLogin.Click += async (s, e) => await WindowHandlerGuard.RunAsync(HandleBrowserLoginAsync, ReportHandlerError);
        accountButtons.Children.Add(_btnLogin);

        _btnLogout = CreateQuietButton("Sair da conta");
        _btnLogout.Margin = new Thickness(8, 0, 0, 0);
        _btnLogout.Click += (s, e) => WindowHandlerGuard.Run(HandleLogout, ReportHandlerError);
        accountButtons.Children.Add(_btnLogout);
        accountContent.Children.Add(accountButtons);
        root.Children.Add(accountCard);

        // 3. Meus plugins card
        var pluginsCard = BuildCard("Meus plugins", out var pluginsContent);
        _pluginsPanel = new StackPanel();
        pluginsContent.Children.Add(_pluginsPanel);
        root.Children.Add(pluginsCard);

        // 4. Neste computador card
        var licenseCard = BuildCard("Neste computador", out var licenseContent);
        _txtLicenseStatus = new TextBlock
        {
            FontSize = 13,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        licenseContent.Children.Add(_txtLicenseStatus);

        _btnSync = CreatePrimaryButton("Atualizar minhas licenças");
        _btnSync.Click += async (s, e) => await WindowHandlerGuard.RunAsync(HandleSyncAsync, ReportHandlerError);
        licenseContent.Children.Add(_btnSync);

        // Collapsed manual activation expander
        var manualExpander = new Expander
        {
            Header = "Tenho uma chave de ativação",
            IsExpanded = false,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Primary),
            Margin = new Thickness(0, 14, 0, 0)
        };
        var manualContent = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        manualContent.Children.Add(new TextBlock
        {
            Text = "Se a sua empresa enviou uma chave (começa com NAEC-...), digite abaixo para liberar.",
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        var keyRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _txtManualKey = new TextBox
        {
            FontSize = 13,
            Padding = new Thickness(10, 8, 10, 8),
            Background = UiTheme.Brush(UiTheme.Card),
            Foreground = UiTheme.Brush(UiTheme.Text),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        Grid.SetColumn(_txtManualKey, 0);
        keyRow.Children.Add(_txtManualKey);

        _btnActivateKey = CreatePrimaryButton("Ativar");
        _btnActivateKey.Margin = new Thickness(8, 0, 0, 0);
        _btnActivateKey.Click += async (s, e) => await WindowHandlerGuard.RunAsync(HandleActivateKeyAsync, ReportHandlerError);
        Grid.SetColumn(_btnActivateKey, 1);
        keyRow.Children.Add(_btnActivateKey);
        manualContent.Children.Add(keyRow);

        _txtMachineId = new TextBlock
        {
            Text = LeaseTrust.TryGetMachineId(out string machineId, out _)
                ? $"Identificação desta máquina (para o suporte): {machineId}"
                : "Identificação desta máquina indisponível (MachineGuid do Windows não pôde ser lido).",
            FontSize = 11,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        manualContent.Children.Add(_txtMachineId);
        manualExpander.Content = manualContent;
        licenseContent.Children.Add(manualExpander);
        root.Children.Add(licenseCard);

        // 5. Atualizações card
        _updatesCard = BuildCard("Atualizações", out var updatesContent);
        _txtInstalledVersion = new TextBlock
        {
            Text = $"Versão instalada: {ConnectorConfig.Version}",
            FontSize = 13,
            Foreground = UiTheme.Brush(UiTheme.Text),
            Margin = new Thickness(0, 0, 0, 8)
        };
        updatesContent.Children.Add(_txtInstalledVersion);

        _txtUpdateStatus = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        updatesContent.Children.Add(_txtUpdateStatus);

        _txtUpdateNotes = new TextBlock
        {
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.Text),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        updatesContent.Children.Add(_txtUpdateNotes);

        var updateButtons = new StackPanel { Orientation = Orientation.Horizontal };
        _btnCheckUpdates = CreatePrimaryButton("Verificar atualizações");
        _btnCheckUpdates.Click += async (s, e) => await WindowHandlerGuard.RunAsync(HandleCheckUpdatesAsync, ReportHandlerError);
        updateButtons.Children.Add(_btnCheckUpdates);

        _btnDownloadInstall = CreatePrimaryButton("Baixar e instalar");
        _btnDownloadInstall.Margin = new Thickness(8, 0, 0, 0);
        _btnDownloadInstall.Visibility = Visibility.Collapsed;
        _btnDownloadInstall.Click += async (s, e) => await WindowHandlerGuard.RunAsync(HandleDownloadAndInstallAsync, ReportHandlerError);
        updateButtons.Children.Add(_btnDownloadInstall);

        updatesContent.Children.Add(updateButtons);
        root.Children.Add(_updatesCard);

        // 6. Feedback message
        _txtFeedback = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_txtFeedback);

        // 7. Footer
        root.Children.Add(BuildFooter());

        RefreshUiFromStorage();
    }

    private FrameworkElement BuildHeader(out TextBlock badgeBlock)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel();
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Minha Conta",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = UiTheme.Brush(UiTheme.Primary)
        });

        badgeBlock = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Accent),
            Margin = new Thickness(0, 2, 0, 2),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };
        titlePanel.Children.Add(badgeBlock);

        titlePanel.Children.Add(new TextBlock
        {
            Text = "Suas licenças da Node.aec em um só lugar.",
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 2, 0, 8)
        });

        titlePanel.Children.Add(new Border
        {
            Background = UiTheme.Brush(UiTheme.Accent),
            Height = 3,
            Width = 48,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(2)
        });
        Grid.SetColumn(titlePanel, 0);
        header.Children.Add(titlePanel);

        var btnCatalog = CreateLinkButton("Ver catálogo ↗");
        btnCatalog.VerticalAlignment = VerticalAlignment.Center;
        btnCatalog.Click += (s, e) => OpenCatalog();
        Grid.SetColumn(btnCatalog, 1);
        header.Children.Add(btnCatalog);

        return header;
    }

    private static FrameworkElement BuildCard(string title, out StackPanel contentPanel)
    {
        var border = new Border
        {
            Background = UiTheme.Brush(UiTheme.Card),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = UiTheme.Brush(UiTheme.Primary),
            Margin = new Thickness(0, 0, 0, 6)
        });

        contentPanel = new StackPanel();
        stack.Children.Add(contentPanel);
        border.Child = stack;
        return border;
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
    /// Refreshes all window sections from local storage and update cache.
    /// Guaranteed noexcept: an escaping exception kills the Revit process.
    /// </summary>
    public void RefreshUiFromStorage()
    {
        try
        {
            RenderUiFromStorage();
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("WARN", $"Falha ao atualizar a UI a partir do armazenamento: {ex.GetType().Name}.");
            try
            {
                _txtLicenseStatus.Text = "Não foi possível atualizar o estado local das licenças.";
                _txtLicenseStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
            }
            catch
            {
            }
        }
    }

    /// <summary>Body of <see cref="RefreshUiFromStorage"/>.</summary>
    private void RenderUiFromStorage()
    {
        // 1. Account section
        var session = LeaseStorage.LoadSession();
        var account = UiState.Account(session?.Name, session?.Email);
        _txtAccountTitle.Text = account.Title;
        _txtAccountHint.Text = account.Hint;
        _btnLogin.Visibility = account.ShowLogin ? Visibility.Visible : Visibility.Collapsed;
        _btnLogout.Visibility = account.ShowLogout ? Visibility.Visible : Visibility.Collapsed;

        // 2. Plugins section & computer status
        string? jwtToken = LeaseStorage.LoadMasterLease();
        var payload = string.IsNullOrWhiteSpace(jwtToken) ? null : LeaseStorage.ParseJwtPayload(jwtToken);
        var (statusText, tone) = UiState.LicenseStatus(jwtToken, payload);
        _txtLicenseStatus.Text = statusText;
        _txtLicenseStatus.Foreground = UiTheme.Brush(tone switch
        {
            LicenseStatusTone.Neutral => UiTheme.TextSecondary,
            LicenseStatusTone.Warning => UiTheme.Accent,
            _ => UiTheme.Primary,
        });

        RenderPlugins(session.HasValue && !string.IsNullOrWhiteSpace(session.Value.Token), payload?.Entitlements);

        // 3. Update status badge
        RenderUpdateBadgeFromStorage();
    }

    private void RenderPlugins(bool isLoggedIn, IReadOnlyList<EntitlementItem>? entitlements)
    {
        _pluginsPanel.Children.Clear();
        var list = entitlements ?? Array.Empty<EntitlementItem>();

        switch (UiState.PluginsBranch(isLoggedIn, list.Count))
        {
            case PluginsView.LoggedOut:
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
                _pluginsPanel.Children.Add(new TextBlock
                {
                    Text = UiState.NoPluginsText,
                    FontSize = 13,
                    Foreground = UiTheme.Brush(UiTheme.TextSecondary),
                    Margin = new Thickness(0, 4, 0, 8),
                    TextWrapping = TextWrapping.Wrap
                });
                var btnCatalogEmpty = CreateLinkButton("Conhecer o catálogo de plugins ↗");
                btnCatalogEmpty.Click += (s, e) => OpenCatalog();
                _pluginsPanel.Children.Add(btnCatalogEmpty);
                return;

            default:
                foreach (var ent in UiState.PluginsActiveFirst(list))
                {
                    _pluginsPanel.Children.Add(BuildPluginCard(ent));
                }
                var btnCatalogMore = CreateLinkButton("Explorar catálogo completo no marketplace ↗");
                btnCatalogMore.Margin = new Thickness(0, 4, 0, 0);
                btnCatalogMore.HorizontalAlignment = HorizontalAlignment.Left;
                btnCatalogMore.Click += (s, e) => OpenCatalog();
                _pluginsPanel.Children.Add(btnCatalogMore);
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

    private void RenderUpdateBadgeFromStorage()
    {
        var status = UpdateStorage.LoadUpdateStatus();
        if (status == null)
        {
            _txtHeaderBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var decision = UpdateDecisions.Evaluate(ConnectorConfig.Version, status.LatestVersion, status.MinimumVersion);
        switch (decision)
        {
            case UpdateDecision.Required:
                _txtHeaderBadge.Text = "Atualização obrigatória";
                _txtHeaderBadge.Visibility = Visibility.Visible;
                if (_latestRelease == null)
                {
                    string minMsg = $"Esta versão do Node.aec Connector ({ConnectorConfig.Version}) não é mais aceita. Atualize para a versão {status.MinimumVersion ?? status.LatestVersion} ou mais recente.";
                    _txtUpdateStatus.Text = minMsg;
                    _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
                }
                break;

            case UpdateDecision.Available:
                _txtHeaderBadge.Text = $"Nova versão disponível: {status.LatestVersion}";
                _txtHeaderBadge.Visibility = Visibility.Visible;
                if (_latestRelease == null)
                {
                    _txtUpdateStatus.Text = $"Nova versão disponível: {status.LatestVersion}. Clique em verificar atualizações para baixar.";
                    _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Primary);
                }
                break;

            default:
                _txtHeaderBadge.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private async Task HandleBrowserLoginAsync()
    {
        SetFeedback("Abrindo o navegador para você entrar com segurança...", UiTheme.Primary);
        _btnLogin.IsEnabled = false;

        try
        {
            var authService = new DesktopAuthService();
            string userToken = await authService.LoginViaBrowserAsync().ConfigureAwait(true);

            SetFeedback("Pronto! Buscando suas licenças...", UiTheme.Primary);
            var client = new ConnectorApiClient();
            var syncResult = await client.SyncMasterEntitlementsAsync(userToken).ConfigureAwait(true);

            if (syncResult.Success)
            {
                var userClaims = LeaseStorage.ParseUserSessionClaims(userToken);
                if (!LeaseStorage.SaveSession(userClaims?.Email, userToken, userClaims?.Name))
                {
                    SetFeedback("Suas licenças chegaram, mas não foi possível salvar a sessão localmente. Verifique as permissões do usuário.", UiTheme.Accent);
                }
                else
                {
                    string? keysWarning = syncResult.VerificationWarning;
                    SetFeedback(
                        keysWarning ?? $"Tudo pronto! {syncResult.GrantedCount} plugin(s) liberado(s) neste computador.",
                        keysWarning == null ? UiTheme.Primary : UiTheme.Accent);
                }
            }
            else
            {
                SetFeedback(syncResult.Message, UiTheme.Accent);
                if (syncResult.IsOutdated)
                {
                    ScrollToUpdatesAndCheck();
                }
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Não foi possível concluir o login: {ex.Message}", UiTheme.Accent);
        }
        finally
        {
            _btnLogin.IsEnabled = true;
            RefreshUiFromStorage();
        }
    }

    private async Task HandleSyncAsync()
    {
        SetFeedback("Atualizando suas licenças...", UiTheme.Primary);
        _btnSync.IsEnabled = false;

        try
        {
            var session = LeaseStorage.LoadSession();
            var client = new ConnectorApiClient();

            SyncResult result;
            if (session.HasValue && !string.IsNullOrWhiteSpace(session.Value.Token))
            {
                result = await client.SyncMasterEntitlementsAsync(session.Value.Token).ConfigureAwait(true);
            }
            else
            {
                result = await client.ValidateHeartbeatAsync().ConfigureAwait(true);
            }

            if (result.Success)
            {
                string? keysWarning = result.VerificationWarning;
                SetFeedback(
                    keysWarning ?? "Licenças atualizadas com sucesso.",
                    keysWarning == null ? UiTheme.Primary : UiTheme.Accent);
            }
            else
            {
                SetFeedback(result.Message, UiTheme.Accent);
                if (result.IsOutdated)
                {
                    ScrollToUpdatesAndCheck();
                }
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Sem conexão no momento: {ex.Message}", UiTheme.Accent);
        }
        finally
        {
            _btnSync.IsEnabled = true;
            RefreshUiFromStorage();
        }
    }

    private async Task HandleActivateKeyAsync()
    {
        string key = _txtManualKey.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            SetFeedback("Digite a chave enviada para você (começa com NAEC-...).", UiTheme.Accent);
            return;
        }

        SetFeedback("Ativando sua chave...", UiTheme.Primary);
        _btnActivateKey.IsEnabled = false;

        try
        {
            var client = new ConnectorApiClient();
            var result = await client.ActivateKeyAsync(key).ConfigureAwait(true);

            if (result.Success)
            {
                _txtManualKey.Clear();

                var session = LeaseStorage.LoadSession();
                if (session.HasValue && !string.IsNullOrWhiteSpace(session.Value.Token))
                {
                    SetFeedback("Chave ativada! Atualizando suas licenças...", UiTheme.Primary);
                    var sync = await client.SyncMasterEntitlementsAsync(session.Value.Token).ConfigureAwait(true);

                    string? keysWarning = sync.VerificationWarning;
                    SetFeedback(
                        sync.Success
                            ? keysWarning ?? $"Chave ativada! {sync.GrantedCount} plugin(s) liberado(s) neste computador."
                            : $"Chave ativada, mas não foi possível atualizar as licenças agora: {sync.Message}",
                        sync.Success && keysWarning == null ? UiTheme.Primary : UiTheme.Accent);

                    if (sync.IsOutdated)
                    {
                        ScrollToUpdatesAndCheck();
                    }
                }
                else
                {
                    SetFeedback("Chave ativada! Entre com sua conta para trazer suas licenças para este computador.", UiTheme.Primary);
                }
            }
            else
            {
                SetFeedback(result.Message, UiTheme.Accent);
                if (result.IsOutdated)
                {
                    ScrollToUpdatesAndCheck();
                }
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Não foi possível ativar agora: {ex.Message}", UiTheme.Accent);
        }
        finally
        {
            _btnActivateKey.IsEnabled = true;
            RefreshUiFromStorage();
        }
    }

    private void HandleLogout()
    {
        var confirm = MessageBox.Show(
            this,
            "Deseja sair da sua conta neste computador?\n\nSeus plugins ficarão bloqueados até o próximo login.",
            "Sair da conta",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            LeaseStorage.ClearAll();
            SetFeedback("Você saiu da conta.", UiTheme.TextSecondary);
            RefreshUiFromStorage();
        }
    }

    private async Task HandleCheckUpdatesAsync()
    {
        _txtUpdateStatus.Text = "Verificando atualizações no servidor Node.aec...";
        _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Primary);
        _btnCheckUpdates.IsEnabled = false;
        _btnDownloadInstall.Visibility = Visibility.Collapsed;
        _txtUpdateNotes.Visibility = Visibility.Collapsed;

        try
        {
            var client = new ConnectorApiClient();
            var release = await client.GetLatestReleaseAsync().ConfigureAwait(true);

            if (release == null)
            {
                _txtUpdateStatus.Text = "Não foi possível verificar atualizações agora. Tente novamente mais tarde.";
                _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
                return;
            }

            _latestRelease = release;
            UpdateStorage.SaveUpdateStatus(release.Version, release.MinimumVersion);

            var decision = UpdateDecisions.Evaluate(ConnectorConfig.Version, release.Version, release.MinimumVersion);
            switch (decision)
            {
                case UpdateDecision.UpToDate:
                    _txtUpdateStatus.Text = "Você está na versão mais recente.";
                    _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Primary);
                    _btnDownloadInstall.Visibility = Visibility.Collapsed;
                    _txtUpdateNotes.Visibility = Visibility.Collapsed;
                    break;

                case UpdateDecision.Available:
                    _txtUpdateStatus.Text = $"Node.aec Connector {release.Version} disponível.";
                    _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Primary);
                    ShowReleaseNotes(release.Notes);
                    _btnDownloadInstall.Visibility = Visibility.Visible;
                    break;

                case UpdateDecision.Required:
                    _txtUpdateStatus.Text = $"Esta versão do Node.aec Connector ({ConnectorConfig.Version}) não é mais aceita pela plataforma. Atualize para a versão {release.MinimumVersion ?? release.Version} ou mais recente.";
                    _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
                    ShowReleaseNotes(release.Notes);
                    _btnDownloadInstall.Visibility = Visibility.Visible;
                    break;

                default:
                    _txtUpdateStatus.Text = "Não foi possível comparar a versão com a do servidor.";
                    _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
                    break;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("WARN", $"Erro na verificação de atualização: {ex.GetType().Name}.");
            _txtUpdateStatus.Text = "Não foi possível verificar atualizações agora. Tente novamente mais tarde.";
            _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
        }
        finally
        {
            _btnCheckUpdates.IsEnabled = true;
            RenderUpdateBadgeFromStorage();
        }
    }

    private void ShowReleaseNotes(string? notes)
    {
        if (!string.IsNullOrWhiteSpace(notes))
        {
            _txtUpdateNotes.Text = notes;
            _txtUpdateNotes.Visibility = Visibility.Visible;
        }
        else
        {
            _txtUpdateNotes.Visibility = Visibility.Collapsed;
        }
    }

    private async Task HandleDownloadAndInstallAsync()
    {
        if (_latestRelease == null)
        {
            await HandleCheckUpdatesAsync();
            return;
        }

        var session = LeaseStorage.LoadSession();
        if (!session.HasValue || string.IsNullOrWhiteSpace(session.Value.Token))
        {
            _txtUpdateStatus.Text = "Entre com sua conta para baixar a atualização.";
            _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
            return;
        }

        _btnDownloadInstall.IsEnabled = false;
        _btnCheckUpdates.IsEnabled = false;
        _txtUpdateStatus.Text = "Solicitando link seguro de download...";
        _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Primary);

        try
        {
            var client = new ConnectorApiClient();
            string? downloadUrl = await client.GetDownloadUrlAsync(
                _latestRelease.Slug,
                _latestRelease.File.VersionPublicId,
                _latestRelease.File.FilePublicId,
                session.Value.Token).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                _txtUpdateStatus.Text = "Não foi possível obter o link de download. Tente novamente mais tarde.";
                _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
                return;
            }

            string targetPath = UpdatePackageVerifier.GetTargetFilePath(_latestRelease.Version);
            _txtUpdateStatus.Text = "Baixando atualização... 0%";

            var progress = new Progress<double>(pct =>
            {
                int percentage = (int)Math.Round(pct * 100);
                _txtUpdateStatus.Text = $"Baixando atualização... {percentage}%";
            });

            bool downloaded = await client.DownloadFileAsync(downloadUrl!, targetPath, _latestRelease.File.SizeBytes, progress).ConfigureAwait(true);
            if (!downloaded)
            {
                _txtUpdateStatus.Text = "Não foi possível concluir o download. Verifique sua conexão e tente novamente.";
                _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
                return;
            }

            _txtUpdateStatus.Text = "Verificando integridade do arquivo...";
            bool valid = UpdatePackageVerifier.VerifyFile(targetPath, _latestRelease.File.SizeBytes, _latestRelease.File.Sha256);
            if (!valid)
            {
                try
                {
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                }
                catch { }

                _txtUpdateStatus.Text = "O arquivo baixado não confere. Nada foi instalado. Tente novamente.";
                _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
                return;
            }

            _txtUpdateStatus.Text = "Iniciando instalador...";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = targetPath,
                    Arguments = "/SILENT /WaitForRevit",
                    UseShellExecute = true
                };
                Process.Start(psi);

                _txtUpdateStatus.Text = "Salve seu trabalho e feche o Revit. A instalação continua sozinha e mantém as versões do Revit que você escolheu.";
                _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Primary);
                _btnDownloadInstall.Visibility = Visibility.Collapsed;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                _txtUpdateStatus.Text = "A instalação foi cancelada. Clique em Baixar e instalar para tentar de novo.";
                _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
                return;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("WARN", $"Falha ao baixar/instalar atualização: {ex.GetType().Name}.");
            _txtUpdateStatus.Text = $"Não foi possível instalar a atualização ({ex.GetType().Name}). Tente novamente.";
            _txtUpdateStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
        }
        finally
        {
            _btnDownloadInstall.IsEnabled = true;
            _btnCheckUpdates.IsEnabled = true;
        }
    }

    private void ScrollToUpdatesAndCheck()
    {
        try
        {
            _updatesCard.BringIntoView();
            _ = WindowHandlerGuard.RunAsync(HandleCheckUpdatesAsync, ReportHandlerError);
        }
        catch
        {
        }
    }

    private void SetFeedback(string message, Color color)
    {
        _txtFeedback.Text = message;
        _txtFeedback.Foreground = UiTheme.Brush(color);
    }

    private void ReportHandlerError(Exception ex)
    {
        SetFeedback(
            $"Ocorreu um erro inesperado ({ex.GetType().Name}). Tente novamente; se persistir, reinicie o Revit.",
            UiTheme.Accent);
    }

    private void OpenCatalog()
    {
        OpenUrl(ConnectorConfig.CatalogUrl);
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetFeedback($"Não foi possível abrir o link ({ex.GetType().Name}).", UiTheme.Accent);
        }
    }

    private static ImageSource? LoadAppIcon()
    {
        try
        {
            string dir = Path.GetDirectoryName(typeof(ConnectorWindow).Assembly.Location)
                         ?? AppDomain.CurrentDomain.BaseDirectory;
            string path = Path.Combine(dir, "Resources", "nodeaec-32.png");
            if (!File.Exists(path)) path = Path.Combine(dir, "nodeaec-32.png");

            if (File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
        }
        catch { }
        return null;
    }

    private static ConnectorWindow? _instance;

    /// <summary>
    /// Opens (or reactivates) the single Connector window. Repeated ribbon clicks do not
    /// stack windows — each one syncs and writes storage in parallel (L11).
    /// Kept public static with exact signature for Lite reflection invocation.
    /// </summary>
    public static void Open()
    {
        if (_instance != null)
        {
            _instance.Activate();
            return;
        }

        _instance = new ConnectorWindow();
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
        _instance.Activate();
    }
}
