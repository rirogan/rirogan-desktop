using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RiroganDesktop;

internal sealed class DesktopSettingsForm : Form
{
    public DesktopPreferences Result { get; }
    public string? CallSection { get; private set; }
    private static readonly Color Background = Color.FromArgb(14, 19, 34);
    private static readonly Color Surface = Color.FromArgb(24, 31, 51);
    private static readonly Color TextLight = Color.FromArgb(232, 237, 249);
    private static readonly Color TextDim = Color.FromArgb(165, 178, 208);
    private static readonly Color Accent = Color.FromArgb(112, 98, 250);

    public DesktopSettingsForm(DesktopPreferences current)
    {
        Result = new DesktopPreferences
        {
            PushToTalk = current.PushToTalk, GlobalShortcuts = current.GlobalShortcuts,
            GameDetection = current.GameDetection, DoNotDisturb = current.DoNotDisturb,
            HidePreviews = current.HidePreviews, EcoMode = current.EcoMode,
            MuteWhileFocused = current.MuteWhileFocused, PushKey = current.PushKey,
            StreamerMode = current.StreamerMode, AllowMicrophone = current.AllowMicrophone,
            AllowCamera = current.AllowCamera, StartMinimized = current.StartMinimized,
            InterfaceScale = current.InterfaceScale,
            DownloadAlerts = current.DownloadAlerts,
            NotifyLargeFiles = current.NotifyLargeFiles, DownloadFolder = current.DownloadFolder,
            MicrophoneKey = current.MicrophoneKey, DeafenKey = current.DeafenKey
        };
        Text = "Rirogan — Preferências";
        Size = new Size(700, 660);
        MinimumSize = new Size(420, 480);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Background;
        ForeColor = TextLight;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;

        var header = new Panel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(24, 16, 18, 8), BackColor = Surface };
        var title = new Label { Text = "CONFIGURAÇÕES", Dock = DockStyle.Top, Height = 36,
            ForeColor = TextLight, Font = new Font("Segoe UI", 19, FontStyle.Bold) };
        var subtitle = new Label { Text = "Preferências do aplicativo e atalhos para as configurações da chamada", Dock = DockStyle.Fill, ForeColor = TextDim };
        header.Controls.Add(subtitle); header.Controls.Add(title);
        Controls.Add(header);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Surface, Padding = new Padding(20, 12, 20, 12) };
        var save = new Button { Text = "Salvar alterações", Dock = DockStyle.Right, Width = 188,
            BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        save.FlatAppearance.BorderSize = 0;
        void Save(string? callSection = null)
        {
            if (Result.GlobalShortcuts && (Result.MicrophoneKey == Result.DeafenKey ||
                (Result.PushToTalk && (Result.PushKey == Result.MicrophoneKey || Result.PushKey == Result.DeafenKey))))
            {
                MessageBox.Show(this, "Escolha teclas diferentes para Push-to-Talk, microfone e áudio.", "Atalhos em conflito", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            CallSection = callSection;
            DialogResult = DialogResult.OK; Close();
        }
        save.Click += (_, _) => Save();
        footer.Controls.Add(save);
        Controls.Add(footer);
        AcceptButton = save;

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 5) };
        Controls.Add(tabs);
        tabs.BringToFront();
        FlowLayoutPanel AddTab(string name)
        {
            var tab = new TabPage(name) { BackColor = Background, ForeColor = TextLight, UseVisualStyleBackColor = false };
            var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true, Padding = new Padding(16, 14, 16, 18) };
            void ResizeRows()
            {
                int width = Math.Max(1, body.ClientSize.Width - body.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
                foreach (Control row in body.Controls) row.Width = width;
            }
            body.Resize += (_, _) => ResizeRows();
            body.ControlAdded += (_, _) => ResizeRows();
            tab.Controls.Add(body);
            tabs.TabPages.Add(tab);
            return body;
        }
        var scroll = AddTab("Áudio");

        void Note(string text, int height = 56)
        {
            scroll.Controls.Add(new Label { Text = text, Height = height, ForeColor = TextDim,
                Padding = new Padding(8, 8, 8, 4), Margin = new Padding(0, 4, 0, 4) });
        }
        void CallSettings(string section)
        {
            var button = new Button { Text = "Salvar e abrir esta aba na chamada", Height = 40,
                BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand, Margin = new Padding(0, 10, 0, 6) };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => Save(section);
            scroll.Controls.Add(button);
        }
        void Toggle(string label, bool initial, Action<bool> onChange)
        {
            var box = new Panel { Height = 51, BackColor = Surface, Margin = new Padding(0, 3, 0, 3), Padding = new Padding(12, 6, 12, 6) };
            var check = new CheckBox { Text = label, Checked = initial, Dock = DockStyle.Fill, ForeColor = TextLight,
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            check.CheckedChanged += (_, _) => onChange(check.Checked);
            box.Controls.Add(check); scroll.Controls.Add(box);
        }
        void Selection(string label, string[] options, int chosen, Action<int> onChange)
        {
            var box = new Panel { Height = 62, BackColor = Surface, Margin = new Padding(0, 3, 0, 3), Padding = new Padding(12, 6, 12, 6) };
            var name = new Label { Text = label, Dock = DockStyle.Left, Width = 245, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextLight, AutoEllipsis = true };
            var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(36, 44, 68), ForeColor = TextLight };
            combo.Items.AddRange(options);
            combo.SelectedIndex = Math.Clamp(chosen, 0, options.Length - 1);
            combo.SelectedIndexChanged += (_, _) => onChange(combo.SelectedIndex);
            box.Controls.Add(combo); box.Controls.Add(name); scroll.Controls.Add(box);
        }

        Note("Volume, saída e processamento do microfone são ajustados na chamada. Os atalhos abaixo são do aplicativo.");
        Toggle("Permitir solicitações de microfone", Result.AllowMicrophone, x => Result.AllowMicrophone = x);
        Toggle("Push-to-Talk global (segure para falar)", Result.PushToTalk, x => Result.PushToTalk = x);
        Toggle("Atalhos globais de microfone e áudio", Result.GlobalShortcuts, x => Result.GlobalShortcuts = x);
        Selection("Tecla Push-to-Talk", new[] { "F6", "F7", "F8", "F9", "F10", "F11", "F12" },
            Result.PushKey - 0x75, i => Result.PushKey = 0x75 + i);
        var functionKeys = new[] { "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12" };
        Selection("Atalho: microfone", functionKeys, Result.MicrophoneKey - 0x70, i => Result.MicrophoneKey = 0x70 + i);
        Selection("Atalho: ensurdecer", functionKeys, Result.DeafenKey - 0x70, i => Result.DeafenKey = 0x70 + i);
        CallSettings("audio");

        scroll = AddTab("Vídeo");
        Note("Escolha a câmera na chamada. A permissão abaixo controla novas solicitações de câmera do aplicativo.");
        Toggle("Permitir solicitações de câmera", Result.AllowCamera, x => Result.AllowCamera = x);
        CallSettings("video");

        scroll = AddTab("Transmissão");
        Note("Resolução, FPS e a oferta de áudio do PC ficam na chamada. Ao transmitir a tela inteira, marque a opção de áudio quando o seletor do Windows a oferecer.", 82);
        CallSettings("share");

        scroll = AddTab("Aparência");
        Note("O zoom do aplicativo amplia ou reduz o conteúdo exibido no WebView. Fundo e aparência do Chat & Call ficam na chamada.");
        var scales = new[] { 80, 90, 100, 110, 125, 150 };
        int scaleIndex = Array.IndexOf(scales, Result.InterfaceScale);
        Selection("Zoom do aplicativo", scales.Select(v => v + "%").ToArray(), scaleIndex >= 0 ? scaleIndex : 2,
            i => Result.InterfaceScale = scales[i]);
        CallSettings("appearance");

        scroll = AddTab("Aplicativo");
        Note("Notificações, atividade de jogos e comportamento da janela.");
        Toggle("Modo streamer — suspender avisos do aplicativo", Result.StreamerMode, x => Result.StreamerMode = x);
        Toggle("Não Perturbe", Result.DoNotDisturb, x => Result.DoNotDisturb = x);
        Toggle("Ocultar prévias de mensagens", Result.HidePreviews, x => Result.HidePreviews = x);
        Toggle("Silenciar avisos com Rirogan em primeiro plano", Result.MuteWhileFocused, x => Result.MuteWhileFocused = x);
        Toggle("Consultar jogos com menos frequência em segundo plano", Result.EcoMode, x => Result.EcoMode = x);
        Toggle("Detectar jogos e publicar no perfil (exige consentimento no site)", Result.GameDetection, x => Result.GameDetection = x);
        Toggle("Iniciar minimizado, quando aberto com o Windows", Result.StartMinimized, x => Result.StartMinimized = x);
        Note("Para mostrar seu jogo no perfil, autorize a presença de jogos em Perfil › Segurança no site.", 48);

        scroll = AddTab("Downloads");
        Toggle("Avisar quando um download terminar", Result.DownloadAlerts, x => Result.DownloadAlerts = x);
        Toggle("Avisar antes de arquivos acima de 100 MB", Result.NotifyLargeFiles, x => Result.NotifyLargeFiles = x);
        var folderPanel = new Panel { Height = 63, BackColor = Surface, Margin = new Padding(0, 4, 0, 4), Padding = new Padding(10) };
        var folderButton = new Button { Text = "Escolher pasta de downloads…", Dock = DockStyle.Left, Width = 270, BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var folderLabel = new Label { Text = string.IsNullOrWhiteSpace(Result.DownloadFolder) ? "Pasta padrão do Windows" : Result.DownloadFolder, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = TextDim, TextAlign = ContentAlignment.MiddleLeft };
        folderButton.Click += (_, _) => {
            using var dialog = new FolderBrowserDialog { Description = "Escolha uma pasta para os downloads do Rirogan", UseDescriptionForTitle = true };
            if (System.IO.Directory.Exists(Result.DownloadFolder)) dialog.SelectedPath = Result.DownloadFolder;
            if (dialog.ShowDialog(this) == DialogResult.OK) { Result.DownloadFolder = dialog.SelectedPath; folderLabel.Text = Result.DownloadFolder; }
        };
        folderPanel.Controls.Add(folderLabel); folderPanel.Controls.Add(folderButton); scroll.Controls.Add(folderPanel);
    }
}
