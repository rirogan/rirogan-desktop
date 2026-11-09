using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace RiroganDesktop;
internal sealed class DownloadsForm : Form
{
    private readonly ListView list = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
    private readonly Dictionary<int, (CoreWebView2DownloadOperation operation, ListViewItem row)> active = new();
    private int sequence;
    public event Action<string>? DownloadCompleted;
    public DownloadsForm()
    {
        Text = "Rirogan — Downloads"; Size = new Size(750, 380);
        BackColor = Color.FromArgb(21, 27, 45); ForeColor = Color.White;
        list.Columns.Add("Arquivo", 255); list.Columns.Add("Progresso", 105);
        list.Columns.Add("Estado", 130); list.Columns.Add("Destino", 200);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Cancelar download", null, (_, _) => { if (TrySelected(out var op)) op.Cancel(); });
        menu.Items.Add("Abrir pasta", null, (_, _) =>
        {
            if (!TrySelected(out var op)) return;
            var path = op.ResultFilePath;
            if (File.Exists(path)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        });
        list.ContextMenuStrip = menu;
        Controls.Add(list);
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }
    private bool TrySelected(out CoreWebView2DownloadOperation operation)
    {
        operation = null!;
        if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not int id || !active.TryGetValue(id, out var value)) return false;
        operation = value.operation; return true;
    }
    public void Track(CoreWebView2DownloadOperation operation)
    {
        int nextId = ++sequence;
        var row = new ListViewItem(Path.GetFileName(operation.ResultFilePath)) { Tag = nextId };
        row.SubItems.Add("0%"); row.SubItems.Add("Baixando"); row.SubItems.Add(operation.ResultFilePath);
        active[nextId] = (operation, row);
        list.Items.Insert(0, row);
        void Update()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(Update)); return; }
            var total = Convert.ToDouble(operation.TotalBytesToReceive);
            row.SubItems[1].Text = total > 0
                ? (100.0 * operation.BytesReceived / total).ToString("F0") + "%" :
                  (operation.BytesReceived / 1048576.0).ToString("F1") + " MB";
            row.SubItems[2].Text = operation.State switch
            {
                CoreWebView2DownloadState.Completed => "Concluído",
                CoreWebView2DownloadState.Interrupted => "Interrompido",
                _ => "Baixando"
            };
        }
        operation.BytesReceivedChanged += (_, _) => Update();
        bool completionEmitted = false;
        operation.StateChanged += (_, _) => {
            Update();
            if (!completionEmitted && operation.State == CoreWebView2DownloadState.Completed)
            {
                completionEmitted = true;
                if (!IsDisposed) DownloadCompleted?.Invoke(operation.ResultFilePath);
            }
        };
        Update();
        if (active.Count > 120)
        {
            for (int i = list.Items.Count - 1; i >= 0 && active.Count > 100; i--)
            {
                if (list.Items[i].Tag is int id && active.TryGetValue(id, out var value) &&
                    value.operation.State != CoreWebView2DownloadState.InProgress)
                { active.Remove(id); list.Items.RemoveAt(i); }
            }
        }
    }
}
