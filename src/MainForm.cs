using System;
using System.Diagnostics;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RiroganDesktop;

internal sealed class MainForm : Form
{
    private const string HomeUrl = "https://rirogan.com/dashboard/mensagens.php";
    private const string LoginUrl = "https://rirogan.com/login.php";
    private const string SiteUrl = "https://rirogan.com";
    private static readonly Color BackgroundColor = Color.FromArgb(14, 19, 34);
    private static readonly Color ControlColor = Color.FromArgb(28, 35, 58);
    private static readonly Color ForegroundColor = Color.FromArgb(230, 236, 250);
    private static readonly Color AccentColor = Color.FromArgb(106, 92, 245);

    private readonly DesktopPreferences desktopPrefs = DesktopPreferences.Read();
    private readonly GlobalPushToTalk pushHook = new();
    private readonly System.Windows.Forms.Timer gameTimer = new() { Interval = 15000 };
    private readonly CallControlForm floatingCall;
    private readonly DownloadsForm downloads = new();
    private DiagnosticsForm? diagnosticsWindow;
    private bool desktopSettingsOpen;
    private readonly System.Windows.Forms.Timer callDetailTimer = new() { Interval = 3500 };
    private readonly System.Windows.Forms.Timer connectivityTimer = new() { Interval = 8000 };
    private bool retryNavigation;
    private int navigationFailures;
    private int browserRecoveryCount;
    private bool browserRecoveryBusy;
    private bool voiceConnected;
    private bool voiceMicEnabled;
    private bool pttHeld;
    private readonly System.Threading.SemaphoreSlim pttCommandGate = new(1, 1);
    private bool applyingPtt;
    private string? pttInitScriptId;
    private string? currentGame;
    private DateTime? gameStartedUtc;
    private DateTime lastGamePublishUtc = DateTime.MinValue;
    private string? lastGamePublished;
    private bool gamePublishBusy;
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = BackgroundColor };
    private readonly Button back = NewButton("‹", "Voltar");
    private readonly Button forward = NewButton("›", "Avançar");
    private readonly Button reload = NewButton("⟳", "Atualizar (Ctrl+R)");
    private readonly Button home = NewButton("⌂", "Chat & Call");
    private readonly Button browser = NewButton("↗", "Abrir página no navegador");
    private readonly Panel sideMenu = new() { Dock = DockStyle.Left, Width = 244, BackColor = Color.FromArgb(19, 25, 44), Visible = false };
    private readonly Button menuToggle = NewButton("☰", "Abrir menu do Rirogan");
    private Button? maxWindowButton;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int ResizeBorder = 7;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int SW_RESTORE = 9;
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    private readonly Label status = new()
    {
        Text = "Iniciando Rirogan…",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(159, 172, 204),
        Padding = new Padding(12, 0, 0, 0),
        AutoEllipsis = true
    };
    private readonly Panel errorPanel = new() { Dock = DockStyle.Fill, BackColor = BackgroundColor, Visible = false };
    private readonly Label errorLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = ForegroundColor,
        Font = new Font("Segoe UI", 12),
        Padding = new Padding(36)
    };
    private readonly NotifyIcon tray;
    private ContextMenuStrip? desktopOptions;
    private readonly ReleaseChannel updater = new();
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 12 * 60 * 60 * 1000 };
    private readonly string startupKeyName = "RiroganDesktop";
    private bool allowTrayMinimize;
    private bool restoringFromTray;
    private bool startWithWindows;
    private bool notificationsEnabled = true;
    private bool updateCheckBusy;
    internal bool RestartForUpdate { get; private set; }
    private const string AppRegistryKey = @"Software\Rirogan\Desktop";
    private bool isClosing;
    private bool webInitialized;
    private string? pendingNotificationUrl;
    private readonly Queue<(string title, string body, string url, string category)> notificationQueue = new();
    private readonly Dictionary<string, DateTime> seenNotifications = new(StringComparer.Ordinal);
    private readonly System.Windows.Forms.Timer notificationTimer = new() { Interval = 2600 };

    public MainForm(bool autostart)
    {
        LoadPreferences();
        floatingCall = new CallControlForm(SendVoiceCommandAsync, SetParticipantVolumeAsync);
        pushHook.Hotkey = (Keys)desktopPrefs.PushKey;
        pushHook.Changed += pressed =>
        {
            if (IsDisposed || isClosing) return;
            if (InvokeRequired) BeginInvoke(new Action(() => OnPushToTalk(pressed)));
            else OnPushToTalk(pressed);
        };
        Text = "Rirogan — Chat & Call";
        MinimumSize = new Size(820, 540);
        Size = new Size(1360, 850);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BackgroundColor;
        ForeColor = ForegroundColor;
        KeyPreview = true;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        // Barra nativa personalizada: sem campos de URL, voltar/avançar nem aparência de navegador.
        FormBorderStyle = FormBorderStyle.None;
        MaximizedBounds = Screen.FromControl(this).WorkingArea;
        var titleBar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(17, 23, 40) };
        var logo = new Label { Text = "RIROGAN", AutoSize = false, Width = 144, Dock = DockStyle.Left,
            Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
        var titleDrag = new Panel { Dock = DockStyle.Fill, BackColor = titleBar.BackColor };
        var windowButtons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 144, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = titleBar.BackColor };
        var minimize = WindowButton("—", "Minimizar");
        maxWindowButton = WindowButton("□", "Maximizar / restaurar");
        var close = WindowButton("×", "Fechar");
        close.MouseEnter += (_, _) => close.BackColor = Color.FromArgb(190, 54, 70);
        close.MouseLeave += (_, _) => close.BackColor = titleBar.BackColor;
        minimize.Click += (_, _) =>
        {
            if (allowTrayMinimize) HideToTray();
            else WindowState = FormWindowState.Minimized;
        };
        maxWindowButton.Click += (_, _) => ToggleMaximize();
        close.Click += (_, _) => Close();
        windowButtons.Controls.Add(minimize);
        windowButtons.Controls.Add(maxWindowButton);
        windowButtons.Controls.Add(close);
        menuToggle.Dock = DockStyle.Left;
        menuToggle.Width = 48;
        menuToggle.Font = new Font("Segoe UI", 16);
        menuToggle.Margin = Padding.Empty;
        menuToggle.Click += (_, _) => { sideMenu.Visible = !sideMenu.Visible; menuToggle.BackColor = sideMenu.Visible ? AccentColor : titleBar.BackColor; };
        titleBar.Controls.Add(titleDrag);
        titleBar.Controls.Add(windowButtons);
        titleBar.Controls.Add(logo);
        titleBar.Controls.Add(menuToggle);
        void Drag(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (e.Clicks >= 2) { ToggleMaximize(); return; }
            if (WindowState == FormWindowState.Maximized) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }
        titleDrag.MouseDown += Drag;
        logo.MouseDown += Drag;
        titleDrag.DoubleClick += (_, _) => ToggleMaximize();
        logo.DoubleClick += (_, _) => ToggleMaximize();
        BuildSidebar();
        errorPanel.Controls.Add(errorLabel);
        Controls.Add(errorPanel);
        Controls.Add(web);
        Controls.Add(sideMenu);
        Controls.Add(titleBar);
        errorPanel.BringToFront();

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Abrir Rirogan", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Alternar microfone (F9)", null, async (_, _) => await SendVoiceCommandAsync("toggleMic"));
        trayMenu.Items.Add("Alternar áudio (F10)", null, async (_, _) => await SendVoiceCommandAsync("toggleDeaf"));
        trayMenu.Items.Add("Encerrar chamada", null, async (_, _) => await SendVoiceCommandAsync("leave"));
        trayMenu.Items.Add("Controles de chamada", null, (_, _) => ShowCallPanel());
        trayMenu.Items.Add("Downloads", null, (_, _) => ShowDownloads());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Configurações", null, (_, _) => OpenDesktopSettings());
        trayMenu.Items.Add("Sair", null, (_, _) => { isClosing = true; Close(); });
        tray = new NotifyIcon
        {
            Icon = Icon,
            Text = "Rirogan",
            ContextMenuStrip = trayMenu,
            Visible = false
        };
        tray.DoubleClick += (_, _) => RestoreFromTray();
        downloads.DownloadCompleted += filename => {
            if (!desktopPrefs.DownloadAlerts || desktopPrefs.StreamerMode || desktopPrefs.DoNotDisturb || !notificationsEnabled) return;
            if (IsDisposed || isClosing) return;
            if (InvokeRequired) { BeginInvoke(new Action(() => NotifyDownloadComplete(filename))); return; }
            NotifyDownloadComplete(filename);
        };
        notificationTimer.Tick += (_, _) => ShowNextNotification();
        tray.BalloonTipClicked += (_, _) =>
        {
            string? target = pendingNotificationUrl;
            pendingNotificationUrl = null;
            RestoreFromTray();
            if (target != null) Navigate(target);
        };
        tray.BalloonTipClosed += (_, _) =>
        {
            if (Visible && WindowState != FormWindowState.Minimized) tray.Visible = false;
        };

        var options = new ContextMenuStrip();
        var trayOption = new ToolStripMenuItem("Minimizar para a bandeja") { CheckOnClick = true, Checked = allowTrayMinimize };
        trayOption.CheckedChanged += (_, _) => { allowTrayMinimize = trayOption.Checked; SavePreferences(); };
        options.Items.Add("Configurações do Desktop…", null, (_, _) => OpenDesktopSettings());
        options.Items.Add("Controles de chamada", null, (_, _) => ShowCallPanel());
        options.Items.Add("Gerenciar downloads", null, (_, _) => ShowDownloads());
        options.Items.Add("Diagnóstico de desempenho", null, (_, _) => ShowDiagnostics());
        options.Items.Add(new ToolStripSeparator());
        options.Items.Add(trayOption);
        var startupOption = new ToolStripMenuItem("Iniciar com o Windows") { Checked = startWithWindows, CheckOnClick = true };
        startupOption.CheckedChanged += (_, _) =>
        {
            if (!SetStartup(startupOption.Checked))
            {
                startupOption.Checked = !startupOption.Checked;
                return;
            }
            startWithWindows = startupOption.Checked;
        };
        options.Items.Add(startupOption);
        var notificationOption = new ToolStripMenuItem("Notificações do Windows") { Checked = notificationsEnabled, CheckOnClick = true };
        notificationOption.CheckedChanged += (_, _) => { notificationsEnabled = notificationOption.Checked; if (!notificationsEnabled) { notificationQueue.Clear(); notificationTimer.Stop(); } SavePreferences(); };
        options.Items.Add(notificationOption);
        options.Items.Add(new ToolStripSeparator());
        options.Items.Add("Verificar atualizações", null, async (_, _) => await CheckUpdatesAsync(true));
        options.Items.Add("Abrir site no navegador", null, (_, _) => OpenExternal(SiteUrl));
        options.Items.Add("Sair", null, (_, _) => { isClosing = true; Close(); });
        // As opções avançadas continuam disponíveis no menu lateral.
        desktopOptions = options;

        Shown += async (_, _) =>
        {
            // Só iniciar oculto quando o Windows inicia o aplicativo com --autostart.
            if (desktopPrefs.StartMinimized && autostart)
            {
                allowTrayMinimize = true;
                HideToTray();
            }
            await InitializeBrowserAsync();
        };
        updateTimer.Tick += async (_, _) => await CheckUpdatesAsync(false);
        updateTimer.Start();
        gameTimer.Tick += (_, _) => ScanGames();
        gameTimer.Start();
        callDetailTimer.Tick += async (_, _) => { if (floatingCall.Visible) await RefreshCallDetailsAsync(); };
        callDetailTimer.Start();
        connectivityTimer.Tick += (_, _) =>
        {
            if (retryNavigation && webInitialized && !voiceConnected && navigationFailures <= 5 &&
                System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            { retryNavigation = false; Navigate(HomeUrl); }
        };
        connectivityTimer.Start();
        HandleCreated += (_, _) => RefreshGlobalHotkeys();
        HandleDestroyed += (_, _) => RemoveGlobalHotkeys();
        Resize += (_, _) =>
        {
            if (allowTrayMinimize && !isClosing && !restoringFromTray && Visible && WindowState == FormWindowState.Minimized)
                HideToTray();
        };
        FormClosing += (_, e) =>
        {
            if (allowTrayMinimize && !isClosing && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
            }
        };
        FormClosed += (_, _) =>
        {
            updateTimer.Stop();
            updateTimer.Dispose();
            notificationTimer.Stop();
            notificationTimer.Dispose();
            tray.Visible = false;
            tray.Dispose();
            gameTimer.Stop(); gameTimer.Dispose();
            RemoveGlobalHotkeys();
            pushHook.Dispose();
            updater.Dispose();
            callDetailTimer.Stop(); callDetailTimer.Dispose();
            connectivityTimer.Stop(); connectivityTimer.Dispose();
            if (diagnosticsWindow is { IsDisposed: false }) diagnosticsWindow.Dispose();
            floatingCall.Dispose(); downloads.Dispose();
        };
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.R) { if (webInitialized) web.Reload(); e.SuppressKeyPress = true; }
            if (e.Alt && e.KeyCode == Keys.Left) { if (webInitialized && web.CanGoBack) web.GoBack(); e.SuppressKeyPress = true; }
            if (e.Alt && e.KeyCode == Keys.Right) { if (webInitialized && web.CanGoForward) web.GoForward(); e.SuppressKeyPress = true; }
        };
        UpdateNavButtons();
        Resize += (_, _) => { if (maxWindowButton != null) maxWindowButton.Text = WindowState == FormWindowState.Maximized ? "❐" : "□"; };
    }

    private static Button WindowButton(string text, string tooltip)
    {
        var b = new Button { Text = text, Size = new Size(48, 48), Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(216, 224, 244),
            BackColor = Color.FromArgb(17, 23, 40), Font = new Font("Segoe UI", 13),
            Cursor = Cursors.Hand, TabStop = false };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 56, 84);
        new ToolTip().SetToolTip(b, tooltip);
        return b;
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        if (maxWindowButton != null) maxWindowButton.Text = WindowState == FormWindowState.Maximized ? "❐" : "□";
    }

    protected override void WndProc(ref Message m)
    {
        // Trata os atalhos globais antes do hit-test da janela sem borda.
        if (m.Msg == WM_HOTKEY && desktopPrefs.GlobalShortcuts && webInitialized)
        {
            if (m.WParam.ToInt32() == 101) _ = SendVoiceCommandAsync("toggleMic");
            if (m.WParam.ToInt32() == 102) _ = SendVoiceCommandAsync("toggleDeaf");
        }

        // Permite redimensionar normalmente uma janela sem borda.
        if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if (m.Result == (IntPtr)HTCLIENT)
            {
                long raw = m.LParam.ToInt64();
                var point = PointToClient(new Point(unchecked((short)(raw & 0xffff)), unchecked((short)((raw >> 16) & 0xffff))));
                bool left = point.X < ResizeBorder, right = point.X >= ClientSize.Width - ResizeBorder;
                bool top = point.Y < ResizeBorder, bottom = point.Y >= ClientSize.Height - ResizeBorder;
                if (left && top) m.Result = (IntPtr)HTTOPLEFT;
                else if (right && top) m.Result = (IntPtr)HTTOPRIGHT;
                else if (left && bottom) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (right && bottom) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)HTLEFT;
                else if (right) m.Result = (IntPtr)HTRIGHT;
                else if (top) m.Result = (IntPtr)HTTOP;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
            }
            return;
        }
        base.WndProc(ref m);
    }

    private void BuildSidebar()
    {
        var scroll = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12, 20, 12, 12),
            BackColor = sideMenu.BackColor };
        sideMenu.Controls.Add(scroll);
        Label Header(string title) => new Label { Text = title.ToUpperInvariant(), Width = 208, Height = 34,
            TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(9, 0, 0, 6),
            ForeColor = Color.FromArgb(144, 155, 184), Font = new Font("Segoe UI", 8, FontStyle.Bold) };
        Button Item(string name, string symbol, Action action, bool accent = false)
        {
            var b = new Button { Text = symbol + "   " + name, TextAlign = ContentAlignment.MiddleLeft,
                Width = 208, Height = 43, Margin = new Padding(0, 2, 0, 2),
                Padding = new Padding(12, 0, 0, 0), BackColor = accent ? AccentColor : Color.FromArgb(28, 35, 58),
                ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(66, 64, 120);
            b.Click += (_, _) => { sideMenu.Visible = false; menuToggle.BackColor = Color.FromArgb(17, 23, 40); action(); };
            return b;
        }
        scroll.Controls.Add(new Label { Text = "SUA COMUNIDADE GAMER", Width = 208, Height = 28,
            ForeColor = Color.FromArgb(163, 177, 213), Font = new Font("Segoe UI", 9) });
        scroll.Controls.Add(Header("Site Rirogan"));
        scroll.Controls.Add(Item("Chat & Call", "◉", () => Navigate(HomeUrl), true));
        scroll.Controls.Add(Item("Feed", "▦", () => Navigate(SiteUrl + "/dashboard/feed.php")));
        scroll.Controls.Add(Item("Arquivos", "▤", () => Navigate(SiteUrl + "/dashboard/arquivos.php")));
        scroll.Controls.Add(Item("Perfil", "♙", () => Navigate(SiteUrl + "/dashboard/perfil.php")));
        scroll.Controls.Add(Header("Aplicativo Windows"));
        scroll.Controls.Add(Item("Configurações", "⚙", OpenDesktopSettings));
        scroll.Controls.Add(Item("Controles de voz", "♫", ShowCallPanel));
        scroll.Controls.Add(Item("Downloads", "↓", ShowDownloads));
        scroll.Controls.Add(Item("Desempenho", "◷", ShowDiagnostics));
        scroll.Controls.Add(Item("Verificar atualizações", "↻", () => { _ = CheckUpdatesAsync(true); }));
        scroll.Controls.Add(Item("Mais opções", "⋯", () => desktopOptions?.Show(this, new Point(55, 52))));
        scroll.Controls.Add(Item("Abrir no navegador", "↗", () => OpenExternal(SiteUrl)));
    }

    private static Button NewButton(string glyph, string tip)
    {
        var b = new Button
        {
            Text = glyph, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            BackColor = ControlColor, ForeColor = ForegroundColor,
            Font = new Font("Segoe UI", 16), Cursor = Cursors.Hand,
            Margin = new Padding(3, 0, 3, 0)
        };
        b.FlatAppearance.BorderSize = 0;
        new ToolTip().SetToolTip(b, tip);
        return b;
    }

    private void ShowError(string message)
    {
        errorLabel.Text = message + "\n\nVerifique a conexão com a internet e tente atualizar (Ctrl+R).";
        errorPanel.Visible = true;
        errorPanel.BringToFront();
        status.Text = "Falha ao abrir o Rirogan";
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rirogan", "WebView2");
            Directory.CreateDirectory(profile);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            await web.EnsureCoreWebView2Async(env);
            ConfigureAccessIdentity();
            webInitialized = true;
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            if (!ConfigurePushToTalk())
                status.Text = "Atalho global indisponível; verifique permissões.";
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            web.ZoomFactor = desktopPrefs.InterfaceScale / 100.0;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            pttInitScriptId = await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(CallPreferencesScript());
            // Script interno: eventos de DMs sem alterações no esquema MySQL.
            // AddScriptToExecuteOnDocumentCreatedAsync executa antes do JS da página.
            await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(LoadNotificationBridge());
            await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(LoadGamePresenceBridge());
            web.CoreWebView2.WebMessageReceived += Browser_WebMessageReceived;
            web.CoreWebView2.NavigationStarting += Browser_NavigationStarting;
            web.CoreWebView2.NavigationStarting += (_, _) => { voiceConnected = false; voiceMicEnabled = false; pttHeld = false; pushHook.Release(); };
            web.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;
            web.CoreWebView2.PermissionRequested += Browser_PermissionRequested;
            // WebView2 exibe as notificações de página usando a UI do Windows.
            // Não interceptamos Notification() nem alteramos lógica de chamadas/DM.
            web.CoreWebView2.NotificationReceived += Browser_NotificationReceived;
            web.CoreWebView2.DownloadStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.DownloadOperation.Uri, UriKind.Absolute, out var source) ||
                    source.Scheme != Uri.UriSchemeHttps) { e.Cancel = true; return; }
                // Downloads externos são possíveis no site, mas nunca são executados automaticamente.
                var selectedFolder = desktopPrefs.DownloadFolder;
                if (!string.IsNullOrWhiteSpace(selectedFolder) && Directory.Exists(selectedFolder))
                {
                    // Usa apenas o nome do arquivo, nunca um caminho fornecido pelo servidor.
                    string safeName = Path.GetFileName(e.ResultFilePath);
                    if (!string.IsNullOrWhiteSpace(safeName))
                        e.ResultFilePath = Path.Combine(selectedFolder, safeName);
                }
                if (desktopPrefs.NotifyLargeFiles && e.DownloadOperation.TotalBytesToReceive > 100L * 1024 * 1024)
                {
                    var answer = MessageBox.Show(this, "Este arquivo tem mais de 100 MB. Deseja continuar o download?", "Download grande — Rirogan", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes) { e.Cancel = true; return; }
                }
                status.Text = "Download iniciado…";
                downloads.Track(e.DownloadOperation);
            };
            web.CoreWebView2.ProcessFailed += (_, e) =>
            {
                // Não reiniciar o aplicativo nem apagar perfis de usuário.
                // Uma falha do processo de renderização pode ser recuperada por Reload.
                if (IsDisposed || isClosing || !IsHandleCreated) return;
                BeginInvoke(new Action(async () =>
                {
                    if (isClosing || IsDisposed) return;
                    ShowError("O WebView2 parou. Tentando recuperar a página com segurança…");
                    if (voiceConnected || browserRecoveryBusy || browserRecoveryCount >= 2) return;
                    browserRecoveryBusy = true;
                    browserRecoveryCount++;
                    try
                    {
                        await Task.Delay(1200);
                        if (!isClosing && webInitialized && !voiceConnected) web.CoreWebView2.Reload();
                    }
                    catch { ShowError("Falha ao recuperar o WebView2. Reinicie o aplicativo."); }
                    finally { browserRecoveryBusy = false; }
                }));
            };
            web.NavigationCompleted += (_, e) =>
            {
                UpdateNavButtons();
                if (e.IsSuccess)
                {
                    errorPanel.Visible = false;
                    navigationFailures = 0; retryNavigation = false; browserRecoveryCount = 0;
                    status.Text = web.Source?.Host ?? "Conectado ao Rirogan";
                    if (desktopPrefs.GameDetection) ScanGames();
                }
                else
                {
                    ShowError("Não foi possível carregar a página. Erro: " + e.WebErrorStatus);
                    if (!voiceConnected && navigationFailures++ < 5) retryNavigation = true;
                }
            };
            web.CoreWebView2.HistoryChanged += (_, _) => UpdateNavButtons();
            web.Source = new Uri(HomeUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowError("O Microsoft Edge WebView2 Runtime não está instalado. Instale-o pelo site oficial da Microsoft:");
            status.Text = "WebView2 Runtime necessário";
            var choice = MessageBox.Show("Este aplicativo precisa do Microsoft Edge WebView2 Runtime. Deseja abrir a página oficial para instalar?", "Rirogan — requisito", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (choice == DialogResult.Yes) OpenExternal("https://developer.microsoft.com/microsoft-edge/webview2/");
        }
        catch (Exception ex)
        {
            ShowError("Falha ao iniciar o navegador integrado: " + ex.GetType().Name);
        }
    }

    private static bool IsTrustedWebsite(Uri uri)
    {
        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               (uri.Host.Equals("rirogan.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("www.rirogan.com", StringComparison.OrdinalIgnoreCase));
    }

    private void Browser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) { e.Cancel = true; return; }
        if (IsTrustedWebsite(uri)) { status.Text = "Carregando…"; return; }
        e.Cancel = true;
        if (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == "mailto")
            OpenExternal(uri.AbsoluteUri);
    }

    private void Browser_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
        if (IsTrustedWebsite(uri)) web.CoreWebView2.Navigate(uri.AbsoluteUri);
        else if (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == "mailto")
            OpenExternal(uri.AbsoluteUri);
    }

    private void Browser_NotificationReceived(object? sender, CoreWebView2NotificationReceivedEventArgs e)
    {
        if (!Uri.TryCreate(e.SenderOrigin, UriKind.Absolute, out var origin) ||
            !IsTrustedWebsite(origin) || !notificationsEnabled || desktopPrefs.DoNotDisturb || desktopPrefs.StreamerMode || desktopPrefs.HidePreviews)
        {
            e.Handled = true;
            // Uma notificação bloqueada não deve ser mostrada pelo host.
            try { e.Notification.ReportClosed(); } catch { }
            return;
        }
        // Handled=false mantém a UI de notificação nativa do WebView2,
        // com clique e fechamento controlados pelo próprio WebView2.
    }

    private static string LoadGamePresenceBridge()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RiroganDesktop.desktop-game-presence.js")
            ?? throw new InvalidOperationException("Integração de presença gamer não incorporada.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string LoadNotificationBridge()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RiroganDesktop.desktop-events.js")
            ?? throw new InvalidOperationException("Script de notificações não incorporado ao executável.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void Browser_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Apenas mensagens originadas nas páginas autenticadas do Rirogan são aceitas.
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var source) || !IsTrustedWebsite(source) ||
            !source.AbsolutePath.StartsWith("/dashboard/", StringComparison.OrdinalIgnoreCase) || isClosing ||
            e.WebMessageAsJson.Length > 8192) return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String) return;
            if (type.GetString() == "rirogan-desktop-voice")
            {
                UpdateVoiceStatus(root);
                return;
            }
            if (type.GetString() != "rirogan-desktop-notification") return;
            if (!notificationsEnabled || desktopPrefs.DoNotDisturb || desktopPrefs.StreamerMode ||
                (desktopPrefs.MuteWhileFocused && Visible && WindowState != FormWindowState.Minimized && ContainsFocus)) return;
            string category = root.GetProperty("category").GetString() ?? "";
            if (category != "dm" && category != "call") return;
            string title = root.GetProperty("title").GetString() ?? "";
            string body = root.GetProperty("body").GetString() ?? "";
            string href = root.GetProperty("href").GetString() ?? "";
            string eventId = root.TryGetProperty("eventId", out var keyElement) && keyElement.ValueKind == JsonValueKind.String
                ? keyElement.GetString() ?? "" : "";
            // Não permitir URL externa, esquemas perigosos, caminhos de API ou outros destinos.
            if (href.Length > 400 || !Uri.TryCreate(new Uri("https://rirogan.com/dashboard/"), href, out var target) ||
                !IsTrustedWebsite(target) || !target.AbsolutePath.Equals("/dashboard/mensagens.php", StringComparison.OrdinalIgnoreCase)) return;
            if (title.Length is < 1 or > 120 || body.Length > 240) return;
            // Deduplicação por ID individual; chamadas têm janela temporal curta por destino.
            string key = category == "dm" && eventId.StartsWith("dm-", StringComparison.Ordinal)
                ? eventId : category + ":" + target.AbsoluteUri;
            DateTime now = DateTime.UtcNow;
            if (seenNotifications.TryGetValue(key, out var when) &&
                (now - when).TotalSeconds < (category == "dm" && eventId.Length > 0 ? 3600 : 25)) return;
            seenNotifications[key] = now;
            if (seenNotifications.Count > 350)
            {
                foreach (var old in new List<string>(seenNotifications.Keys))
                    if ((now - seenNotifications[old]).TotalHours >= 1) seenNotifications.Remove(old);
                if (seenNotifications.Count > 350) seenNotifications.Clear();
            }
            // Não abrir mais de um balão por vez: fila limitada para evitar spam visual.
            if (notificationQueue.Count >= 15) notificationQueue.Dequeue();
            if (desktopPrefs.HidePreviews && category == "dm") body = "Você recebeu uma nova mensagem.";
            notificationQueue.Enqueue((title, body, target.AbsoluteUri, category));
            if (!notificationTimer.Enabled) ShowNextNotification();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            // Payload inesperado não interrompe o WebView2.
        }
    }

    private void NotifyDownloadComplete(string filename)
    {
        if (isClosing || IsDisposed) return;
        tray.Visible = true;
        tray.BalloonTipTitle = "Download concluído";
        tray.BalloonTipText = Path.GetFileName(filename);
        tray.BalloonTipIcon = ToolTipIcon.Info;
        tray.ShowBalloonTip(5000);
    }

    private void ShowNextNotification()
    {
        notificationTimer.Stop();
        if (!notificationsEnabled || desktopPrefs.DoNotDisturb || desktopPrefs.StreamerMode || isClosing || IsDisposed)
        {
            notificationQueue.Clear();
            return;
        }
        if (notificationQueue.Count == 0) return;
        var item = notificationQueue.Dequeue();
        pendingNotificationUrl = item.url;
        tray.Visible = true;
        tray.BalloonTipTitle = item.title;
        tray.BalloonTipText = string.IsNullOrWhiteSpace(item.body) ? "Nova notificação do Rirogan." : item.body;
        tray.BalloonTipIcon = item.category == "call" ? ToolTipIcon.Info : ToolTipIcon.None;
        tray.ShowBalloonTip(item.category == "call" ? 12000 : 6000);
        notificationTimer.Start();
    }

    private void Browser_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !IsTrustedWebsite(uri))
        {
            e.State = CoreWebView2PermissionState.Deny;
            return;
        }
        if ((!notificationsEnabled || desktopPrefs.DoNotDisturb || desktopPrefs.HidePreviews) && e.PermissionKind == CoreWebView2PermissionKind.Notifications)
        {
            e.State = CoreWebView2PermissionState.Deny;
            return;
        }
        if (e.PermissionKind == CoreWebView2PermissionKind.Microphone && !desktopPrefs.AllowMicrophone)
        { e.State = CoreWebView2PermissionState.Deny; return; }
        if (e.PermissionKind == CoreWebView2PermissionKind.Camera && !desktopPrefs.AllowCamera)
        { e.State = CoreWebView2PermissionState.Deny; return; }
        // Deixa o WebView2 solicitar a autorização de microfone, câmera e notificações.
        // getDisplayMedia usa o seletor nativo de compartilhamento de tela do WebView2.
        e.State = CoreWebView2PermissionState.Default;
    }

    private void UpdateNavButtons()
    {
        back.Enabled = webInitialized && web.CanGoBack;
        forward.Enabled = webInitialized && web.CanGoForward;
        reload.Enabled = webInitialized;
        home.Enabled = webInitialized;
    }

    private void Navigate(string url)
    {
        if (webInitialized) web.CoreWebView2.Navigate(url);
    }

    private static void OpenExternal(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == "mailto"))
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { /* A falha ao abrir um link externo não deve interromper o app. */ }
    }

    private void LoadPreferences()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppRegistryKey);
            allowTrayMinimize = key?.GetValue("MinimizeToTray") is int trayPref && trayPref == 1;
            notificationsEnabled = key?.GetValue("Notifications") is not int notificationPref || notificationPref != 0;
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            startWithWindows = run?.GetValue(startupKeyName) is string cmd &&
                cmd.Contains(Program.LauncherPath, StringComparison.OrdinalIgnoreCase);
        }
        catch { /* preferências opcionais */ }
    }

    private void SavePreferences()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppRegistryKey);
            key?.SetValue("MinimizeToTray", allowTrayMinimize ? 1 : 0, RegistryValueKind.DWord);
            key?.SetValue("Notifications", notificationsEnabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { /* preferências opcionais */ }
    }

    private bool SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key is null) return false;
            if (enable) key.SetValue(startupKeyName, "\"" + Program.LauncherPath + "\" --autostart");
            else key.DeleteValue(startupKeyName, false);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não foi possível alterar a inicialização: " + ex.Message,
                "Rirogan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private void ConfigureAccessIdentity()
    {
        var core = web.CoreWebView2;
        core.Settings.UserAgent += " RiroganDesktop/" + Program.AppVersion;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppRegistryKey);
            if (key is null) return;
            if (!Guid.TryParseExact(key.GetValue("InstallationId") as string, "N", out var id))
            {
                id = Guid.NewGuid();
                key.SetValue("InstallationId", id.ToString("N"), RegistryValueKind.String);
            }

            var cookie = core.CookieManager.CreateCookie("__Host-rirogan_desktop_id", id.ToString("N"), "rirogan.com", "/");
            cookie.IsSecure = true;
            cookie.IsHttpOnly = true;
            cookie.SameSite = CoreWebView2CookieSameSiteKind.Lax;
            cookie.Expires = DateTime.UtcNow.AddDays(365);
            core.CookieManager.AddOrUpdateCookie(cookie);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Identificador local indisponível: " + ex.Message);
        }
    }

    private async Task CheckUpdatesAsync(bool manual)
    {
        if (updateCheckBusy || IsDisposed || isClosing) return;
        updateCheckBusy = true;
        try
        {
            var release = await updater.ReadVersionAsync(default);
            if (IsDisposed || isClosing) return;
            if (release < Program.AppVersion)
                throw new InvalidDataException("O canal publicou uma versão anterior à atual.");
            if (release == Program.AppVersion)
            {
                if (manual) MessageBox.Show(this, $"O Rirogan {Program.AppVersion} está atualizado.",
                    "Rirogan — Atualizações", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var answer = MessageBox.Show(this,
                $"O Rirogan {release} está disponível. Deseja reiniciar agora para atualizar? Encerre chamadas e salve seu trabalho antes de continuar.",
                "Atualização disponível", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            RestartForUpdate = true;
            isClosing = true;
            Close();
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !isClosing && manual)
                MessageBox.Show(this, "Não foi possível verificar a versão oficial.\n\n" + ex.Message,
                    "Rirogan — Atualizações", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            updateCheckBusy = false;
        }
    }

    private void HideToTray()
    {
        tray.Visible = true;
        Hide();
    }

    internal void RestoreFromTray()
    {
        if (isClosing || IsDisposed || restoringFromTray) return;
        restoringFromTray = true;
        try
        {
            if (!Visible) Show();
            // A janela pode estar visível para o WinForms e ainda minimizada no Windows.
            if (IsIconic(Handle)) ShowWindow(Handle, SW_RESTORE);

            var titleBarBounds = new Rectangle(Left, Top, Width, Math.Min(48, Height));
            if (!Array.Exists(Screen.AllScreens, screen => screen.WorkingArea.IntersectsWith(titleBarBounds)))
            {
                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                MaximizedBounds = area;
                WindowState = FormWindowState.Normal;
                Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2),
                                     area.Top + Math.Max(0, (area.Height - Height) / 2));
            }

            Activate();
            if (Visible && !IsIconic(Handle)) tray.Visible = false;
        }
        finally
        {
            restoringFromTray = false;
        }
    }

    private async void OpenDesktopSettings()
    {
        // Modal settings must never be launched twice (e.g. tray + sidebar).
        if (desktopSettingsOpen || isClosing) return;
        desktopSettingsOpen = true;
        DesktopPreferences values;
        string? callSection;
        try
        {
            using var window = new DesktopSettingsForm(desktopPrefs);
            if (window.ShowDialog(this) != DialogResult.OK) return;
            values = window.Result;
            callSection = window.CallSection;
        }
        finally
        {
            desktopSettingsOpen = false;
        }

        desktopPrefs.PushToTalk = values.PushToTalk;
        desktopPrefs.GlobalShortcuts = values.GlobalShortcuts;
        desktopPrefs.GameDetection = values.GameDetection;
        desktopPrefs.DoNotDisturb = values.DoNotDisturb;
        desktopPrefs.HidePreviews = values.HidePreviews;
        desktopPrefs.EcoMode = values.EcoMode;
        desktopPrefs.MuteWhileFocused = values.MuteWhileFocused;
        desktopPrefs.PushKey = values.PushKey;
        desktopPrefs.StreamerMode = values.StreamerMode;
        desktopPrefs.AllowMicrophone = values.AllowMicrophone;
        desktopPrefs.AllowCamera = values.AllowCamera;
        desktopPrefs.StartMinimized = values.StartMinimized;
        desktopPrefs.InterfaceScale = values.InterfaceScale;
        desktopPrefs.DownloadAlerts = values.DownloadAlerts;
        desktopPrefs.NotifyLargeFiles = values.NotifyLargeFiles;
        desktopPrefs.DownloadFolder = values.DownloadFolder;
        desktopPrefs.MicrophoneKey = values.MicrophoneKey;
        desktopPrefs.DeafenKey = values.DeafenKey;
        if (webInitialized) web.ZoomFactor = desktopPrefs.InterfaceScale / 100.0;
        if (desktopPrefs.StreamerMode) { notificationQueue.Clear(); notificationTimer.Stop(); pendingNotificationUrl = null; }
        desktopPrefs.Save();
        if (!ConfigurePushToTalk())
            MessageBox.Show("Não foi possível ativar o atalho global. O Push-to-Talk do site continua disponível.");
        if (pushHook.Enabled && voiceConnected) await ApplyPttStateAsync();
        RefreshGlobalHotkeys();
        await UpdatePttBootstrapAsync();
        ScanGames();
        if (callSection is not null) await OpenCallSettingsAsync(callSection);
    }

    private bool ConfigurePushToTalk()
    {
        pushHook.Enabled = false;
        pushHook.Release();
        pttHeld = false;
        pushHook.Hotkey = (Keys)desktopPrefs.PushKey;
        pushHook.Enabled = desktopPrefs.PushToTalk && pushHook.Start();
        return !desktopPrefs.PushToTalk || pushHook.Enabled;
    }

    private string CallPreferencesScript()
    {
        string noise = desktopPrefs.LegacyNoiseSuppression is bool oldNoise
            ? "try { if (localStorage.getItem('rirogan_audio_noiseSuppression') === null) " +
              "localStorage.setItem('rirogan_audio_noiseSuppression','" + (oldNoise ? "1" : "0") + "'); } catch (_) {}"
            : "";
        return "(()=>{if(window!==window.top||location.protocol!=='https:'||" +
            "!['rirogan.com','www.rirogan.com'].includes(location.host)||" +
            "location.pathname!=='/dashboard/mensagens.php')return;" +
            "window.__riroganDesktopPttEnabled=" + (pushHook.Enabled ? "true" : "false") + ";" +
            noise + "window.dispatchEvent(new Event('rirog-desktop-ptt-changed'));})()";
    }

    private async Task UpdatePttBootstrapAsync()
    {
        if (!webInitialized || isClosing) return;
        try
        {
            string script = CallPreferencesScript();
            if (pttInitScriptId != null) web.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(pttInitScriptId);
            pttInitScriptId = await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
            await web.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch { /* página mudou durante atualização de preferências */ }
    }

    private async Task OpenCallSettingsAsync(string tab)
    {
        if (isClosing) return;
        if (webInitialized && web.Source is Uri page && IsTrustedWebsite(page) &&
            page.AbsolutePath.Equals("/dashboard/mensagens.php", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string result = await web.CoreWebView2.ExecuteScriptAsync(
                    "window.RirogCallPage?.desktop?.openSettings(" + JsonSerializer.Serialize(tab) + ") === true");
                if (result == "true")
                {
                    if (!Visible || WindowState == FormWindowState.Minimized) RestoreFromTray();
                    web.Focus();
                    return;
                }
            }
            catch (InvalidOperationException) { /* o WebView foi recriado ou está fechando */ }
            catch (COMException) { /* o WebView mudou de página */ }
        }
        if (!isClosing)
            MessageBox.Show(this, "Entre em uma chamada e use a engrenagem ou abra esta aba novamente pelo menu do Desktop. Instale o patch do site para habilitar o atalho.",
                "Configurações da chamada", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RemoveGlobalHotkeys()
    {
        if (!IsHandleCreated) return;
        UnregisterHotKey(Handle, 101);
        UnregisterHotKey(Handle, 102);
    }
    private void RefreshGlobalHotkeys()
    {
        if (!IsHandleCreated) return;
        RemoveGlobalHotkeys();
        if (!desktopPrefs.GlobalShortcuts) return;
        RegisterHotKey(Handle, 101, MOD_NOREPEAT, (uint)desktopPrefs.MicrophoneKey);
        RegisterHotKey(Handle, 102, MOD_NOREPEAT, (uint)desktopPrefs.DeafenKey);
    }
    private async Task SendVoiceCommandAsync(string action)
    {
        if (!webInitialized || isClosing || !voiceConnected ||
            !new[] { "toggleMic", "toggleDeaf", "leave", "mute", "unmute" }.Contains(action)) return;
        try
        {
            string script = "(async()=>{const d=window.RirogCallPage?.desktop;if(!d)return false;" +
                "switch('" + action + "'){case 'toggleMic':return await d.toggleMic();" +
                "case 'toggleDeaf':return await d.toggleDeaf();case 'leave':return await d.leave();" +
                "case 'mute':return await d.setMic(false);case 'unmute':return await d.setMic(true);}})()";
            await web.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch { /* navegação ou encerramento da chamada */ }
    }
    private void OnPushToTalk(bool pressed)
    {
        pttHeld = pressed;
        if (!pushHook.Enabled || !voiceConnected) return;
        _ = ApplyPttStateAsync();
    }

    // Serializa as alterações do microfone: pressionar/soltar rapidamente a tecla
    // não pode terminar deixando uma ordem antiga de "ativar" em execução.
    private async Task ApplyPttStateAsync()
    {
        await pttCommandGate.WaitAsync();
        try
        {
            if (!pushHook.Enabled || !voiceConnected || isClosing) return;
            await SendVoiceCommandAsync(pttHeld ? "unmute" : "mute");
        }
        finally { pttCommandGate.Release(); }
    }
    private void UpdateVoiceStatus(JsonElement payload)
    {
        try
        {
            bool active = payload.GetProperty("active").GetBoolean();
            bool mic = payload.GetProperty("mic").GetBoolean();
            bool becameActive = active && !voiceConnected;
            voiceConnected = active; voiceMicEnabled = mic;
            if (!active)
            {
                pushHook.Release(); pttHeld = false; floatingCall.Hide(); return;
            }
            // A janela de áudio abre SOMENTE por solicitação do usuário no menu.
            if (pushHook.Enabled && !pttHeld && mic && !applyingPtt)
            {
                applyingPtt = true;
                _ = ForcePttMuteAsync();
            }
            if (becameActive) status.Text = "Em chamada • Rirogan";
        }
        catch { /* status incompleto */ }
    }
    private async Task ForcePttMuteAsync()
    {
        try { await ApplyPttStateAsync(); }
        finally { applyingPtt = false; }
    }
    private void ScanGames()
    {
        string? found = desktopPrefs.GameDetection ? GameDetection.FindActive() : null;
        bool changed = found != currentGame;
        if (changed)
        {
            currentGame = found;
            gameStartedUtc = found is null ? null : DateTime.UtcNow;
            tray.Text = string.IsNullOrWhiteSpace(found) ? "Rirogan" : "Rirogan • " + found[..Math.Min(50, found.Length)];
        }
        // Publicação automática: opt-in duplo (Desktop + consentimento no site).
        // Heartbeat limitado; sem aplicativo ativo ou sem conexão, status expira no servidor.
        bool shouldPublish = desktopPrefs.GameDetection || lastGamePublished != null;
        if (shouldPublish && (changed || (DateTime.UtcNow-lastGamePublishUtc).TotalSeconds >= 40))
        {
            lastGamePublishUtc = DateTime.UtcNow;
            _ = PublishGamePresenceAsync(found);
        }
        gameTimer.Interval = desktopPrefs.EcoMode && !Visible ? 30000 : 15000;
    }

    private async Task PublishGamePresenceAsync(string? game)
    {
        if (gamePublishBusy || !webInitialized || isClosing || IsDisposed) return;
        if (!Uri.TryCreate(web.Source?.AbsoluteUri, UriKind.Absolute, out var page) ||
            !IsTrustedWebsite(page) || !page.AbsolutePath.StartsWith("/dashboard/", StringComparison.OrdinalIgnoreCase)) return;
        gamePublishBusy = true;
        try
        {
            int elapsed = game is null || gameStartedUtc is null ? 0 :
                Math.Clamp((int)(DateTime.UtcNow-gameStartedUtc.Value).TotalSeconds,0,604800);
            var payload = JsonSerializer.Serialize(new { game=game??"", elapsed });
            string result = await web.CoreWebView2.ExecuteScriptAsync(
                "window.riroganDesktopPublishGame ? window.riroganDesktopPublishGame(" + payload + ") : null");
            // Store last sent value only when the endpoint confirms successful delivery.
            using var outer = JsonDocument.Parse(result);
            if (outer.RootElement.ValueKind==JsonValueKind.Object &&
                outer.RootElement.TryGetProperty("ok",out var ok) && ok.ValueKind==JsonValueKind.True)
                lastGamePublished = game;
        }
        catch (Exception) { /* Sem interromper jogos ou chamadas por falhas de rede */ }
        finally { gamePublishBusy = false; }
    }
    // Centraliza a abertura das janelas auxiliares. Nunca chama Show() em
    // um Form já visível, evitando InvalidOperationException no segundo clique.
    private void ShowOrActivateWindow(Form window)
    {
        if (isClosing || IsDisposed || Disposing || window.IsDisposed || window.Disposing) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ShowOrActivateWindow(window)));
            return;
        }

        if (window.WindowState == FormWindowState.Minimized)
            window.WindowState = FormWindowState.Normal;

        // Garante tamanho utilizável e posição acessível mesmo com DPI alto
        // ou quando o segundo monitor tiver sido desconectado.
        var available = Screen.FromControl(this).WorkingArea;
        const int gap = 12;
        if (!window.Visible)
        {
            int width = Math.Min(window.Width, Math.Max(1, available.Width - 2 * gap));
            int height = Math.Min(window.Height, Math.Max(1, available.Height - 2 * gap));
            // Não reduzir abaixo do mínimo declarado pelo próprio formulário.
            if (width >= window.MinimumSize.Width && height >= window.MinimumSize.Height)
                window.Size = new Size(width, height);
            window.StartPosition = FormStartPosition.Manual;
            window.Location = new Point(
                available.Left + Math.Max(gap, (available.Width - window.Width) / 2),
                available.Top + Math.Max(gap, (available.Height - window.Height) / 2));
            window.Show(this);
        }
        else
        {
            // Se está aberta, somente coloca em primeiro plano.
            window.BringToFront();
            window.Activate();
        }
    }

    private void ShowCallPanel()
    {
        if (!voiceConnected)
        {
            MessageBox.Show("Entre em uma chamada para usar os controles flutuantes.", "Rirogan");
            return;
        }
        if (floatingCall.IsDisposed || isClosing) return;
        // Mantém o posicionamento próprio da janela de chamadas.
        floatingCall.EnsureOnScreen(this);
        ShowOrActivateWindow(floatingCall);
        _ = RefreshCallDetailsAsync();
    }

    private void ShowDownloads() => ShowOrActivateWindow(downloads);

    private void ShowDiagnostics()
    {
        if (isClosing) return;
        if (diagnosticsWindow is null || diagnosticsWindow.IsDisposed)
        {
            diagnosticsWindow = new DiagnosticsForm(
                () => voiceConnected ? "Em chamada" : (retryNavigation ? "Reconectando" : status.Text));
            diagnosticsWindow.FormClosed += (_, _) => diagnosticsWindow = null;
        }
        ShowOrActivateWindow(diagnosticsWindow);
    }

    private async Task SetParticipantVolumeAsync(string identity, double volume)
    {
        if (!voiceConnected || !webInitialized || identity.Length > 100 || volume < 0 || volume > 1) return;
        try
        {
            string script = "window.RirogCallPage?.desktop?.setVolume(" + JsonSerializer.Serialize(identity) + "," +
                volume.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
            await web.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch { /* chamada encerrada */ }
    }
    private async Task RefreshCallDetailsAsync()
    {
        if (!webInitialized || !voiceConnected || !floatingCall.Visible || isClosing) return;
        try
        {
            var json = await web.CoreWebView2.ExecuteScriptAsync(
                "JSON.stringify({state:window.RirogCallPage?.desktop?.connection?.()||'disconnected'," +
                "users:window.RirogCallPage?.desktop?.participants?.()||[]})");
            var contents = JsonSerializer.Deserialize<string>(json);
            if (contents == null) return;
            using var doc = JsonDocument.Parse(contents);
            string state = doc.RootElement.GetProperty("state").GetString() ?? "unknown";
            var people = new List<CallParticipant>();
            foreach (var item in doc.RootElement.GetProperty("users").EnumerateArray())
            {
                if (people.Count >= 60) break;
                if (item.TryGetProperty("id", out var id) && id.GetString() is string identity && identity.Length <= 100 && identity.Length > 0)
                    people.Add(new CallParticipant {
                        Id = identity, Name = item.GetProperty("name").GetString() ?? "Participante",
                        Quality = item.GetProperty("quality").GetString() ?? "unknown",
                        Volume = item.GetProperty("volume").GetDouble()
                    });
            }
            if (!isClosing && !floatingCall.IsDisposed) floatingCall.UpdateCall(state, people);
        }
        catch { /* mudança de página ou chamada */ }
    }
}
