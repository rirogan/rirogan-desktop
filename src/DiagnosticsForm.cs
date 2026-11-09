using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace RiroganDesktop;
internal sealed class DiagnosticsForm : Form
{
    private readonly Label details = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 10), ForeColor = Color.White, Padding = new Padding(14) };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private TimeSpan oldCpu;
    private DateTime oldTime = DateTime.UtcNow;
    private readonly Func<string> getStatus;
    public DiagnosticsForm(Func<string> getStatus)
    {
        this.getStatus = getStatus;
        Text = "Rirogan — Diagnóstico local"; Size = new Size(540, 290);
        BackColor = Color.FromArgb(21, 27, 45); ForeColor = Color.White;
        Controls.Add(details);
        timer.Tick += (_, _) => RefreshInfo();
        Shown += (_, _) => { timer.Start(); RefreshInfo(); };
        FormClosed += (_, _) => { timer.Stop(); timer.Dispose(); };
    }
    private void RefreshInfo()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var now = DateTime.UtcNow;
            var elapsed = Math.Max(0.001, (now - oldTime).TotalSeconds);
            var cpu = Math.Max(0, (process.TotalProcessorTime - oldCpu).TotalSeconds) / elapsed / Environment.ProcessorCount * 100.0;
            oldCpu = process.TotalProcessorTime; oldTime = now;
            details.Text = $"Rirogan Desktop 1.5\n\n" +
                $"CPU (processo principal): {cpu:F1}%\n" +
                $"Memória privada: {process.PrivateMemorySize64 / 1048576d:F0} MB\n" +
                $"Memória de trabalho: {process.WorkingSet64 / 1048576d:F0} MB\n" +
                $"WebView2 Runtime: {CoreWebView2Environment.GetAvailableBrowserVersionString()}\n" +
                $"Estado: {getStatus()}\n\n" +
                "O WebView2 pode usar processos adicionais. Nenhuma mensagem privada é registrada.";
        }
        catch { details.Text = "Diagnóstico indisponível neste momento."; }
    }
}
