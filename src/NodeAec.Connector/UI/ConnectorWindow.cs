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
/// The single window of the Node.aec Connector for Autodesk Revit ("Node.aec Connector").
/// Top to bottom: a new-version banner (only when one exists), the account row, the license
/// status strip, the plugin list, the collapsed key activation, and a fixed footer with the
/// version and update check. Built in code with the Node.aec palette (<see cref="UiTheme"/>).
/// </summary>
public class ConnectorWindow : Window
{
    /// <summary>Height of one plugin row; the list scrolls inside itself past <see cref="VisiblePluginRows"/> rows.</summary>
    private const double PluginRowHeight = 40;

    /// <summary>Rows shown before the plugin list scrolls on its own.</summary>
    private const int VisiblePluginRows = 8;

    private static readonly ControlTemplate FlatButtonTemplate = BuildFlatButtonTemplate();

    private readonly Border _updateBanner;
    private readonly TextBlock _txtUpdateTitle;
    private readonly TextBlock _txtUpdateNotes;
    private readonly Button _btnDownloadInstall;
    private readonly TextBlock _txtUpdateStatus;

    private readonly Grid _accountSignedIn;
    private readonly StackPanel _accountSignedOut;
    private readonly TextBlock _txtInitials;
    private readonly TextBlock _txtAccountTitle;
    private readonly TextBlock _txtAccountHint;
    private readonly TextBlock _txtSignedOutTitle;
    private readonly TextBlock _txtSignedOutHint;
    private readonly Button _btnLogin;
    private readonly Button _btnLogout;

    private readonly Border _statusStrip;
    private readonly Border _statusDot;
    private readonly TextBlock _txtLicenseStatus;
    private readonly Button _btnSync;

    private readonly TextBlock _txtPluginsSummary;
    private readonly Grid _pluginFilter;
    private readonly TextBox _txtPluginFilter;
    private readonly StackPanel _pluginsPanel;

    private readonly TextBox _txtManualKey;
    private readonly Button _btnActivateKey;

    private readonly TextBlock _txtFeedback;
    private readonly TextBlock _txtFooterUpdate;
    private readonly Button _btnCheckUpdates;

    private IReadOnlyList<EntitlementItem> _shownPlugins = Array.Empty<EntitlementItem>();
    private LatestReleaseInfo? _latestRelease;

    public ConnectorWindow()
    {
        Title = "Node.aec Connector";
        Width = 560;
        Height = 700;
        MinWidth = 480;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = UiTheme.Brush(UiTheme.Canvas);
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

        // The footer and the feedback line stay put; only the content above them scrolls,
        // and only when the window is shorter than its content.
        var layout = new DockPanel { LastChildFill = true };
        Content = layout;

        var footer = BuildFooter(out _txtFooterUpdate, out _btnCheckUpdates);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);

        _txtFeedback = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(20, 0, 20, 10),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(_txtFeedback, Dock.Bottom);
        layout.Children.Add(_txtFeedback);

        var content = new StackPanel { Margin = new Thickness(20, 18, 20, 12) };
        layout.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        });

        // 1. New-version banner (hidden until the check on open finds one)
        _updateBanner = BuildUpdateBanner(out _txtUpdateTitle, out _txtUpdateNotes, out _btnDownloadInstall, out _txtUpdateStatus);
        content.Children.Add(_updateBanner);

        // 2. Account row
        var accountPanel = new Border
        {
            Background = UiTheme.Brush(UiTheme.Card),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var accountHost = new Grid();
        accountPanel.Child = accountHost;

        _accountSignedIn = new Grid();
        _accountSignedIn.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _accountSignedIn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _accountSignedIn.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _txtInitials = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var initialsBadge = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = UiTheme.Brush(UiTheme.Primary),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _txtInitials
        };
        Grid.SetColumn(initialsBadge, 0);
        _accountSignedIn.Children.Add(initialsBadge);

        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _txtAccountTitle = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Text),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _txtAccountHint = new TextBlock
        {
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        identity.Children.Add(_txtAccountTitle);
        identity.Children.Add(_txtAccountHint);
        Grid.SetColumn(identity, 1);
        _accountSignedIn.Children.Add(identity);

        _btnLogout = CreateLinkButton("Sair da conta");
        _btnLogout.VerticalAlignment = VerticalAlignment.Center;
        _btnLogout.Margin = new Thickness(12, 0, 0, 0);
        _btnLogout.Click += (s, e) => WindowHandlerGuard.Run(HandleLogout, ReportHandlerError);
        Grid.SetColumn(_btnLogout, 2);
        _accountSignedIn.Children.Add(_btnLogout);
        accountHost.Children.Add(_accountSignedIn);

        _accountSignedOut = new StackPanel();
        _txtSignedOutTitle = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Text),
            TextWrapping = TextWrapping.Wrap
        };
        _txtSignedOutHint = new TextBlock
        {
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 4, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        _btnLogin = CreatePrimaryButton("Entrar com minha conta");
        _btnLogin.HorizontalAlignment = HorizontalAlignment.Left;
        _btnLogin.Click += async (s, e) => await WindowHandlerGuard.RunAsync(HandleBrowserLoginAsync, ReportHandlerError);
        _accountSignedOut.Children.Add(_txtSignedOutTitle);
        _accountSignedOut.Children.Add(_txtSignedOutHint);
        _accountSignedOut.Children.Add(_btnLogin);
        accountHost.Children.Add(_accountSignedOut);
        content.Children.Add(accountPanel);

        // 3. License status strip: the "am I OK?" answer
        _statusStrip = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 10, 10),
            Margin = new Thickness(0, 0, 0, 20)
        };
        var statusGrid = new Grid();
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _statusDot = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(4.5),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_statusDot, 0);
        statusGrid.Children.Add(_statusDot);
        _txtLicenseStatus = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(_txtLicenseStatus, 1);
        statusGrid.Children.Add(_txtLicenseStatus);
        _btnSync = CreateSecondaryButton("Atualizar licenças");
        _btnSync.Margin = new Thickness(12, 0, 0, 0);
        _btnSync.VerticalAlignment = VerticalAlignment.Center;
        _btnSync.ToolTip = "Busca suas licenças na plataforma Node.aec agora.";
        _btnSync.Click += async (s, e) => await WindowHandlerGuard.RunAsync(HandleSyncAsync, ReportHandlerError);
        Grid.SetColumn(_btnSync, 2);
        statusGrid.Children.Add(_btnSync);
        _statusStrip.Child = statusGrid;
        content.Children.Add(_statusStrip);

        // 4. Meus plugins: heading, summary, optional filter, compact rows
        var pluginsHeader = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        pluginsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pluginsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pluginsTitle = new TextBlock
        {
            Text = "Meus plugins",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Text),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(pluginsTitle, 0);
        pluginsHeader.Children.Add(pluginsTitle);
        var btnCatalog = CreateLinkButton("Ver catálogo ↗");
        btnCatalog.VerticalAlignment = VerticalAlignment.Center;
        btnCatalog.Click += (s, e) => OpenCatalog();
        Grid.SetColumn(btnCatalog, 1);
        pluginsHeader.Children.Add(btnCatalog);
        content.Children.Add(pluginsHeader);

        _txtPluginsSummary = new TextBlock
        {
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        content.Children.Add(_txtPluginsSummary);

        _pluginFilter = BuildPluginFilter(out _txtPluginFilter);
        _txtPluginFilter.TextChanged += (s, e) => WindowHandlerGuard.Run(RenderPluginRows, ReportHandlerError);
        content.Children.Add(_pluginFilter);

        _pluginsPanel = new StackPanel();
        content.Children.Add(new Border
        {
            Background = UiTheme.Brush(UiTheme.Card),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 16),
            Child = new ScrollViewer
            {
                MaxHeight = PluginRowHeight * VisiblePluginRows,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _pluginsPanel
            }
        });

        // 5. Key activation, collapsed: few people need it
        var manualExpander = new Expander
        {
            Header = "Tenho uma chave de ativação",
            IsExpanded = false,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Primary)
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
            FontWeight = FontWeights.Normal,
            Padding = new Thickness(10, 7, 10, 7),
            Background = UiTheme.Brush(UiTheme.Card),
            Foreground = UiTheme.Brush(UiTheme.Text),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
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

        manualContent.Children.Add(new TextBox
        {
            Text = LeaseTrust.TryGetMachineId(out string machineId, out _)
                ? $"Identificação desta máquina (para o suporte): {machineId}"
                : "Identificação desta máquina indisponível (MachineGuid do Windows não pôde ser lido).",
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 11,
            FontWeight = FontWeights.Normal,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            TextWrapping = TextWrapping.Wrap,
            ToolTip = "Selecione e copie para enviar ao suporte."
        });
        manualExpander.Content = manualContent;
        content.Children.Add(manualExpander);

        RefreshUiFromStorage();

        // Every opening checks for a newer Connector, so users are offered the update when
        // they use it. One GET off the UI thread; nothing runs at Revit startup or on a timer.
        Loaded += async (s, e) => await WindowHandlerGuard.RunAsync(HandleCheckUpdatesAsync, ReportHandlerError);
    }

    private Border BuildUpdateBanner(out TextBlock title, out TextBlock notes, out Button download, out TextBlock status)
    {
        var stack = new StackPanel();
        title = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Primary),
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(title);

        notes = new TextBlock
        {
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.Text),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        stack.Children.Add(notes);

        download = CreatePrimaryButton("Baixar e instalar");
        download.HorizontalAlignment = HorizontalAlignment.Left;
        download.Margin = new Thickness(0, 10, 0, 0);
        download.Click += async (s, e) => await WindowHandlerGuard.RunAsync(HandleDownloadAndInstallAsync, ReportHandlerError);
        stack.Children.Add(download);

        status = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = UiTheme.Brush(UiTheme.Primary),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        stack.Children.Add(status);

        return new Border
        {
            Background = UiTheme.Brush(UiTheme.InfoSoft),
            BorderBrush = UiTheme.Brush(UiTheme.InfoBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 12),
            Visibility = Visibility.Collapsed,
            Child = stack
        };
    }

    private static Grid BuildPluginFilter(out TextBox box)
    {
        var host = new Grid { Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        box = new TextBox
        {
            FontSize = 13,
            Padding = new Thickness(10, 6, 10, 6),
            Background = UiTheme.Brush(UiTheme.Card),
            Foreground = UiTheme.Brush(UiTheme.Text),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        // TextBox has no placeholder in WPF: a hint drawn on top, hidden once there is text.
        var hint = new TextBlock
        {
            Text = "Filtrar plugins pelo nome",
            FontSize = 13,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(13, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        TextBox filterBox = box;
        filterBox.TextChanged += (s, e) => hint.Visibility = string.IsNullOrEmpty(filterBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        host.Children.Add(box);
        host.Children.Add(hint);
        return host;
    }

    private FrameworkElement BuildFooter(out TextBlock updateText, out Button checkAgain)
    {
        var footer = new Border
        {
            Background = UiTheme.Brush(UiTheme.Card),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 10, 20, 10)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = $"Node.aec Connector {ConnectorConfig.Version}",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Text)
        });
        var updateLine = new StackPanel { Orientation = Orientation.Horizontal };
        updateText = new TextBlock
        {
            FontSize = 11,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        updateLine.Children.Add(updateText);
        checkAgain = CreateLinkButton("Verificar atualizações");
        checkAgain.FontSize = 11;
        checkAgain.Margin = new Thickness(8, 0, 0, 0);
        checkAgain.VerticalAlignment = VerticalAlignment.Center;
        checkAgain.Click += async (s, e) => await WindowHandlerGuard.RunAsync(HandleCheckUpdatesAsync, ReportHandlerError);
        updateLine.Children.Add(checkAgain);
        info.Children.Add(updateLine);
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var btnClose = CreateSecondaryButton("Fechar");
        btnClose.VerticalAlignment = VerticalAlignment.Center;
        btnClose.Click += (s, e) => Close();
        Grid.SetColumn(btnClose, 1);
        grid.Children.Add(btnClose);

        footer.Child = grid;
        return footer;
    }

    /// <summary>
    /// One flat template for every button: a rounded border filled from the button's own
    /// Background/BorderBrush, dimmed on hover and when disabled. Replaces the stock Aero
    /// chrome, whose light-blue hover ignores the palette.
    /// </summary>
    private static ControlTemplate BuildFlatButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "Chrome" };
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85, "Chrome"));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5, "Chrome"));
        template.Triggers.Add(hover);
        template.Triggers.Add(disabled);
        template.Seal();
        return template;
    }

    private static Button CreatePrimaryButton(string content)
    {
        return new Button
        {
            Template = FlatButtonTemplate,
            Content = content,
            Background = UiTheme.Brush(UiTheme.Primary),
            BorderBrush = UiTheme.Brush(UiTheme.Primary),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White,
            Padding = new Thickness(16, 8, 16, 8),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand
        };
    }

    private static Button CreateSecondaryButton(string content)
    {
        return new Button
        {
            Template = FlatButtonTemplate,
            Content = content,
            Background = UiTheme.Brush(UiTheme.Card),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(1),
            Foreground = UiTheme.Brush(UiTheme.Primary),
            Padding = new Thickness(14, 7, 14, 7),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand
        };
    }

    private static Button CreateLinkButton(string content)
    {
        return new Button
        {
            Template = FlatButtonTemplate,
            Content = content,
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.Primary),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2),
            Cursor = System.Windows.Input.Cursors.Hand
        };
    }

    /// <summary>
    /// Refreshes all window sections from local storage.
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
                RenderStatusStrip("Não foi possível atualizar o estado local das licenças.", LicenseStatusTone.Warning);
            }
            catch
            {
            }
        }
    }

    /// <summary>Body of <see cref="RefreshUiFromStorage"/>.</summary>
    private void RenderUiFromStorage()
    {
        // 1. Account row
        var session = LeaseStorage.LoadSession();
        var account = UiState.Account(session?.Name, session?.Email);
        _accountSignedIn.Visibility = account.ShowLogout ? Visibility.Visible : Visibility.Collapsed;
        _accountSignedOut.Visibility = account.ShowLogin ? Visibility.Visible : Visibility.Collapsed;
        if (account.ShowLogout)
        {
            _txtInitials.Text = UiState.Initials(session?.Name, session?.Email);
            _txtAccountTitle.Text = account.Title;
            _txtAccountHint.Text = account.Hint;
            _txtAccountHint.Visibility = string.IsNullOrEmpty(account.Hint) ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            _txtSignedOutTitle.Text = account.Title;
            _txtSignedOutHint.Text = account.Hint;
        }

        // 2. Status strip & plugins
        string? jwtToken = LeaseStorage.LoadMasterLease();
        var payload = string.IsNullOrWhiteSpace(jwtToken) ? null : LeaseStorage.ParseJwtPayload(jwtToken);
        var (statusText, tone) = UiState.LicenseStatus(jwtToken, payload);
        RenderStatusStrip(statusText, tone);

        bool isLoggedIn = session.HasValue && !string.IsNullOrWhiteSpace(session.Value.Token);
        // Signed out with no local licenses there is nothing to refresh: sign-in comes first.
        _btnSync.Visibility = isLoggedIn || !string.IsNullOrWhiteSpace(jwtToken) ? Visibility.Visible : Visibility.Collapsed;

        RenderPlugins(isLoggedIn, payload?.Entitlements);
    }

    private void RenderStatusStrip(string text, LicenseStatusTone tone)
    {
        (Color fill, Color border, Color ink) = tone switch
        {
            LicenseStatusTone.Ok => (UiTheme.GoodSoft, UiTheme.GoodBorder, UiTheme.Good),
            LicenseStatusTone.Warning => (UiTheme.WarningSoft, UiTheme.WarningBorder, UiTheme.Warning),
            _ => (UiTheme.Card, UiTheme.Border, UiTheme.TextSecondary),
        };
        _statusStrip.Background = UiTheme.Brush(fill);
        _statusStrip.BorderBrush = UiTheme.Brush(border);
        _statusDot.Background = UiTheme.Brush(ink);
        _txtLicenseStatus.Text = text;
        _txtLicenseStatus.Foreground = UiTheme.Brush(ink);
    }

    private void RenderPlugins(bool isLoggedIn, IReadOnlyList<EntitlementItem>? entitlements)
    {
        var list = UiState.PluginsToShow(entitlements);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        switch (UiState.PluginsBranch(isLoggedIn, list.Count))
        {
            case PluginsView.LoggedOut:
                _shownPlugins = Array.Empty<EntitlementItem>();
                _txtPluginsSummary.Visibility = Visibility.Collapsed;
                _pluginFilter.Visibility = Visibility.Collapsed;
                ShowPluginsMessage(UiState.LoggedOutPluginsText);
                return;

            case PluginsView.Empty:
                _shownPlugins = Array.Empty<EntitlementItem>();
                _txtPluginsSummary.Visibility = Visibility.Collapsed;
                _pluginFilter.Visibility = Visibility.Collapsed;
                ShowPluginsMessage(UiState.NoPluginsText);
                return;

            default:
                _shownPlugins = UiState.PluginsAttentionFirst(list, now);
                _txtPluginsSummary.Text = UiState.PluginsSummary(_shownPlugins, now);
                _txtPluginsSummary.Visibility = Visibility.Visible;
                _pluginFilter.Visibility = _shownPlugins.Count >= UiState.FilterThreshold ? Visibility.Visible : Visibility.Collapsed;
                if (_pluginFilter.Visibility != Visibility.Visible)
                {
                    _txtPluginFilter.Text = string.Empty;
                }

                RenderPluginRows();
                return;
        }
    }

    /// <summary>Rebuilds the rows from <see cref="_shownPlugins"/> and the filter text.</summary>
    private void RenderPluginRows()
    {
        if (_shownPlugins.Count == 0)
        {
            return;
        }

        var rows = UiState.FilterPlugins(_shownPlugins, _txtPluginFilter.Text);
        if (rows.Count == 0)
        {
            ShowPluginsMessage(UiState.NoFilterMatchText);
            return;
        }

        _pluginsPanel.Children.Clear();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 0; i < rows.Count; i++)
        {
            _pluginsPanel.Children.Add(BuildPluginRow(rows[i], now, isLast: i == rows.Count - 1));
        }
    }

    private void ShowPluginsMessage(string text)
    {
        _pluginsPanel.Children.Clear();
        _pluginsPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(14, 12, 14, 12),
            TextWrapping = TextWrapping.Wrap
        });
    }

    private FrameworkElement BuildPluginRow(EntitlementItem item, DateTimeOffset now, bool isLast)
    {
        string name = string.IsNullOrWhiteSpace(item.Name) ? item.Slug : item.Name;
        var (statusText, tone) = UiState.PluginStatus(item, now);
        (Color fill, Color ink) = tone switch
        {
            PluginTone.Ok => (UiTheme.GoodSoft, UiTheme.Good),
            PluginTone.Expiring => (UiTheme.WarningSoft, UiTheme.Warning),
            _ => (UiTheme.DangerSoft, UiTheme.Danger),
        };

        var row = new Grid { MinHeight = PluginRowHeight };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameBlock = new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Text),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = name
        };
        Grid.SetColumn(nameBlock, 0);
        row.Children.Add(nameBlock);

        var pill = new Border
        {
            Background = UiTheme.Brush(fill),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 3),
            Margin = new Thickness(10, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = statusText,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.Brush(ink)
            }
        };
        Grid.SetColumn(pill, 1);
        row.Children.Add(pill);

        string productUrl = ProductLinks.BuildProductUrl(item.Slug);
        var btnProduct = CreateLinkButton("↗");
        btnProduct.FontSize = 14;
        btnProduct.Padding = new Thickness(6, 2, 6, 2);
        btnProduct.VerticalAlignment = VerticalAlignment.Center;
        btnProduct.ToolTip = "Abrir página do produto";
        btnProduct.Click += (s, e) => OpenUrl(productUrl);
        Grid.SetColumn(btnProduct, 2);
        row.Children.Add(btnProduct);

        return new Border
        {
            Padding = new Thickness(14, 0, 8, 0),
            BorderBrush = UiTheme.Brush(UiTheme.Divider),
            BorderThickness = new Thickness(0, 0, 0, isLast ? 0 : 1),
            Child = row
        };
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
        _txtFooterUpdate.Text = "Verificando atualizações...";
        _btnCheckUpdates.Visibility = Visibility.Collapsed;

        try
        {
            var client = new ConnectorApiClient();
            var release = await client.GetLatestReleaseAsync().ConfigureAwait(true);

            if (release == null)
            {
                _txtFooterUpdate.Text = "Não foi possível verificar atualizações agora.";
                return;
            }

            _latestRelease = release;

            switch (UpdateDecisions.Evaluate(ConnectorConfig.Version, release.Version))
            {
                case UpdateDecision.UpToDate:
                    _txtFooterUpdate.Text = "Você está na versão mais recente.";
                    _updateBanner.Visibility = Visibility.Collapsed;
                    break;

                case UpdateDecision.Available:
                    _txtFooterUpdate.Text = $"Versão {release.Version} disponível.";
                    _txtUpdateTitle.Text = $"Nova versão {release.Version} disponível";
                    _txtUpdateNotes.Text = release.Notes ?? string.Empty;
                    _txtUpdateNotes.Visibility = string.IsNullOrWhiteSpace(release.Notes) ? Visibility.Collapsed : Visibility.Visible;
                    _btnDownloadInstall.Visibility = Visibility.Visible;
                    _txtUpdateStatus.Visibility = Visibility.Collapsed;
                    _updateBanner.Visibility = Visibility.Visible;
                    break;

                default:
                    _txtFooterUpdate.Text = "Não foi possível comparar a versão com a do servidor.";
                    break;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("WARN", $"Erro na verificação de atualização: {ex.GetType().Name}.");
            _txtFooterUpdate.Text = "Não foi possível verificar atualizações agora.";
        }
        finally
        {
            _btnCheckUpdates.Visibility = Visibility.Visible;
        }
    }

    private async Task HandleDownloadAndInstallAsync()
    {
        if (_latestRelease == null)
        {
            await HandleCheckUpdatesAsync();
            return;
        }

        // Every message of this flow is shown under the button, inside the banner.
        _txtUpdateStatus.Visibility = Visibility.Visible;

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
