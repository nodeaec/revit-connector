using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using NodeAec.Connector.Auth;
using NodeAec.Connector.Client;
using NodeAec.Connector.Config;
using NodeAec.Connector.Hardware;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.UI;

/// <summary>
/// "Minha Conta" window of the Node.aec Connector for Autodesk Revit.
/// Wording aimed at architects (no technical jargon): sign in, sign out, and refresh
/// licenses. Manual key activation sits collapsed in a closed expander.
/// Visual identity follows the Node.aec web light mode.
/// </summary>
public class ConnectorWindow : Window
{
    private readonly TextBlock _txtAccountTitle;
    private readonly TextBlock _txtAccountHint;
    private readonly Button _btnLogin;
    private readonly Button _btnLogout;
    private readonly TextBlock _txtLicenseStatus;
    private readonly Button _btnSync;
    private readonly TextBox _txtManualKey;
    private readonly Button _btnActivateKey;
    private readonly TextBlock _txtMachineId;
    private readonly TextBlock _txtFeedback;

    public ConnectorWindow()
    {
        Title = "Minha Conta — Node.aec";
        Width = 560;
        Height = 700;
        MinWidth = 520;
        MinHeight = 600;
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
        root.Children.Add(BuildHeader());

        // 2. Account card (sign in / sign out)
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

        // 3. Licenses-on-this-computer card
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
        root.Children.Add(licenseCard);

        // 4. Collapsed manual activation (keeps the main UI uncluttered)
        var manualExpander = new Expander
        {
            Header = "Tenho uma chave de ativação",
            IsExpanded = false,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Primary),
            Margin = new Thickness(0, 0, 0, 16)
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

        // .lease file import stays out of this iteration: the export/exchange format
        // is not a stable contract yet, and a file of unknown origin would be
        // rejected by the gate at signature validation.
        _txtMachineId = new TextBlock
        {
            Text = HardwareId.TryGetMachineId(out string machineId, out _)
                ? $"Identificação desta máquina (para o suporte): {machineId}"
                : "Identificação desta máquina indisponível (MachineGuid do Windows não pôde ser lido).",
            FontSize = 11,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        manualContent.Children.Add(_txtMachineId);
        manualExpander.Content = manualContent;
        root.Children.Add(manualExpander);

        // 5. Feedback message
        _txtFeedback = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_txtFeedback);

        // 6. Footer
        root.Children.Add(BuildFooter());

        RefreshUiFromStorage();
    }

    private FrameworkElement BuildHeader()
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
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Suas licenças da Node.aec em um só lugar.",
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 4, 0, 8)
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

    private FrameworkElement BuildCard(string title, out StackPanel contentPanel)
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
    /// Refreshes the account/license counters from local storage. Called from the
    /// constructor and from the async handlers' <c>finally</c> blocks, so it is
    /// <b>noexcept</b> by contract (H1): an exception here would escape through the WPF
    /// dispatcher as a <c>DispatcherUnhandledException</c> and kill the Revit process.
    /// </summary>
    public void RefreshUiFromStorage()
    {
        try
        {
            RenderUiFromStorage();
        }
        catch (Exception ex)
        {
            // State rendering is best-effort: logs the cause and leaves a static
            // fallback, never propagating into the calling `finally`.
            Diagnostics.ConnectorLog.Write("WARN", $"Falha ao atualizar a UI a partir do armazenamento: {ex.GetType().Name}.");
            try
            {
                _txtLicenseStatus.Text = "Não foi possível atualizar o estado local das licenças.";
                _txtLicenseStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
            }
            catch
            {
                // Window possibly already disposed: nothing left to report from here.
            }
        }
    }

    /// <summary>Body of <see cref="RefreshUiFromStorage"/> — split out so the shell is the only exception zone.</summary>
    private void RenderUiFromStorage()
    {
        // M7: the pure mappings (account and status) live in UiState and are tested
        // headless; only the WPF element assignments stay here.
        var session = LeaseStorage.LoadSession();
        var account = UiState.Account(session?.Name, session?.Email);
        _txtAccountTitle.Text = account.Title;
        _txtAccountHint.Text = account.Hint;
        _btnLogin.Visibility = account.ShowLogin ? Visibility.Visible : Visibility.Collapsed;
        _btnLogout.Visibility = account.ShowLogout ? Visibility.Visible : Visibility.Collapsed;

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
    }

    private async System.Threading.Tasks.Task HandleBrowserLoginAsync()
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
                // Displayed identity comes from the session token claims — the master lease
                // carries no identity (only the technical `sub`).
                var userClaims = LeaseStorage.ParseUserSessionClaims(userToken);

                if (!LeaseStorage.SaveSession(userClaims?.Email, userToken, userClaims?.Name))
                {
                    SetFeedback("Suas licenças chegaram, mas não foi possível salvar a sessão localmente. Verifique as permissões do usuário.", UiTheme.Accent);
                }
                else
                {
                    // M1: surfaces the key-degradation warning when present, instead of
                    // reporting pure success for a lease the gate would later reject.
                    string? keysWarning = syncResult.VerificationWarning;
                    SetFeedback(
                        keysWarning ?? $"Tudo pronto! {syncResult.GrantedCount} plugin(s) liberado(s) neste computador.",
                        keysWarning == null ? UiTheme.Primary : UiTheme.Accent);
                }
            }
            else
            {
                SetFeedback($"Não foi possível buscar suas licenças: {syncResult.Message}", UiTheme.Accent);
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

    private async System.Threading.Tasks.Task HandleSyncAsync()
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
                SetFeedback($"Não foi possível atualizar agora: {result.Message}", UiTheme.Accent);
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

    private async System.Threading.Tasks.Task HandleActivateKeyAsync()
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

                // Activation itself does not write the master lease (single-product token).
                // Now we resync with the account so the master lease picks up the
                // newly activated key — with no session there is no master lease to refresh.
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
                }
                else
                {
                    SetFeedback("Chave ativada! Entre com sua conta para trazer suas licenças para este computador.", UiTheme.Primary);
                }
            }
            else
            {
                SetFeedback(result.Message, UiTheme.Accent);
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
        // N2: owner overload — the dialog stays modal to the window and centered over it.
        var confirm = MessageBox.Show(
            this,
            "Deseja sair da sua conta neste computador?\n\nSeus plugins ficarão bloqueados até o próximo login.",
            "Sair da conta",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            // M7: a single (tested) primitive wipes lease + session — signing out must never
            // leave the product lease behind.
            LeaseStorage.ClearAll();
            SetFeedback("Você saiu da conta.", UiTheme.TextSecondary);
            RefreshUiFromStorage();
        }
    }

    private void SetFeedback(string message, Color color)
    {
        _txtFeedback.Text = message;
        _txtFeedback.Foreground = UiTheme.Brush(color);
    }

    /// <summary>
    /// Reports to the user a handler failure that would have taken the dispatcher down — fixed,
    /// sanitized text (type name, never message content: the detail goes to the log).
    /// Never throws; used as the <see cref="WindowHandlerGuard"/> callback.
    /// </summary>
    private void ReportHandlerError(Exception ex)
    {
        SetFeedback(
            $"Ocorreu um erro inesperado ({ex.GetType().Name}). Tente novamente; se persistir, reinicie o Revit.",
            UiTheme.Accent);
    }

    private void OpenCatalog()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ConnectorConfig.CatalogUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            // N3: browser failure never passes silently — feedback in the window itself.
            SetFeedback($"Não foi possível abrir o catálogo ({ex.GetType().Name}).", UiTheme.Accent);
        }
    }

    private static System.Windows.Media.ImageSource? LoadAppIcon()
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
