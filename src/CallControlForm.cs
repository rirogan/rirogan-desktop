using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RiroganDesktop;

internal sealed class CallParticipant
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Volume { get; set; } = 1;
    public string Quality { get; set; } = "unknown";
}

internal sealed class CallControlForm : Form
{
    private readonly Func<string, Task> command;
    private readonly Func<string, double, Task> volume;
    private readonly ListView peers = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, Dock = DockStyle.Fill };
    private readonly TrackBar participantVolume = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Value = 100, Dock = DockStyle.Fill };
    private readonly Label quality = new() { Text = "Aguardando chamada", ForeColor = Color.White, Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Dictionary<string, CallParticipant> items = new(StringComparer.Ordinal);
    private bool syncing;
    public CallControlForm(Func<string, Task> command, Func<string, double, Task> volume)
    {
        this.command = command; this.volume = volume;
        Text = "Rirogan — Chamada";
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(480, 530); MinimumSize = new Size(390, 360);
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Location = new Point(Math.Max(area.Left, area.Right - Width - 20), Math.Max(area.Top, area.Bottom - Height - 30));
        FormBorderStyle = FormBorderStyle.SizableToolWindow; TopMost = true;
        BackColor = Color.FromArgb(21, 27, 45); ForeColor = Color.White;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 4 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        Controls.Add(panel);
        panel.Controls.Add(quality, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        void Add(string title, string action)
        {
            var b = new Button { Text = title, AutoSize = true, BackColor = Color.FromArgb(48, 54, 82), ForeColor = Color.White };
            b.Click += async (_, _) => await this.command(action);
            buttons.Controls.Add(b);
        }
        Add("Microfone", "toggleMic"); Add("Áudio", "toggleDeaf"); Add("Sair", "leave");
        panel.Controls.Add(buttons, 0, 1);
        peers.Columns.Add("Participante", 250); peers.Columns.Add("Conexão", 120);
        panel.Controls.Add(peers, 0, 2);
        var volPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        volPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        volPanel.Controls.Add(new Label { Text = "Volume do participante selecionado", Dock = DockStyle.Fill }, 0, 0);
        volPanel.Controls.Add(participantVolume, 0, 1);
        panel.Controls.Add(volPanel, 0, 3);
        peers.SelectedIndexChanged += (_, _) =>
        {
            if (peers.SelectedItems.Count == 0) return;
            var id = peers.SelectedItems[0].Name;
            if (items.TryGetValue(id, out var item)) { syncing = true; participantVolume.Value = (int)Math.Clamp(Math.Round(item.Volume * 100), 0, 100); syncing = false; }
        };
        participantVolume.MouseUp += async (_, _) => await CommitVolume();
        participantVolume.KeyUp += async (_, _) => await CommitVolume();
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }
    // Mantém os controles visíveis em monitores pequenos, secundários e em alta escala de DPI.
    public void EnsureOnScreen(Form owner)
    {
        var working = Screen.FromControl(owner).WorkingArea;
        const int margin = 12;
        var maxWidth = Math.Max(1, working.Width - margin * 2);
        var maxHeight = Math.Max(1, working.Height - margin * 2);
        if (Width > maxWidth || Height > maxHeight)
            Size = new Size(Math.Min(Width, maxWidth), Math.Min(Height, maxHeight));

        var desired = Location;
        if (!Visible)
            desired = new Point(working.Right - Width - margin, working.Bottom - Height - margin);
        Location = new Point(
            Math.Clamp(desired.X, working.Left, Math.Max(working.Left, working.Right - Width)),
            Math.Clamp(desired.Y, working.Top, Math.Max(working.Top, working.Bottom - Height)));
    }
    private async Task CommitVolume()
    {
        if (syncing || peers.SelectedItems.Count == 0) return;
        var id = peers.SelectedItems[0].Name;
        await volume(id, participantVolume.Value / 100.0);
    }
    public void UpdateCall(string state, IReadOnlyList<CallParticipant> participants)
    {
        quality.Text = state == "reconnecting" ? "Reconectando à chamada…" : "Conectado à chamada";
        var selected = peers.SelectedItems.Count > 0 ? peers.SelectedItems[0].Name : "";
        items.Clear();
        foreach (var item in participants.Where(p => !string.IsNullOrWhiteSpace(p.Id)).Take(60)) items[item.Id] = item;
        peers.BeginUpdate();
        peers.Items.Clear();
        foreach (var item in items.Values)
        {
            var row = new ListViewItem(item.Name) { Name = item.Id };
            row.SubItems.Add(item.Quality);
            peers.Items.Add(row);
            if (item.Id == selected) row.Selected = true;
        }
        peers.EndUpdate();
    }
}
