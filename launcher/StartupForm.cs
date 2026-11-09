using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RiroganDesktop;

internal sealed class StartupForm : Form
{
    private readonly Label message = new()
    {
        TextAlign = ContentAlignment.MiddleCenter,
        Dock = DockStyle.Fill,
        Padding = new Padding(14, 4, 14, 4)
    };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee };
    private readonly Button retry = new() { Text = "Tentar novamente", AutoSize = true, Visible = false };
    private readonly Button exit = new() { Text = "Sair", AutoSize = true };
    private readonly Icon? emblem = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
    private readonly CancellationTokenSource cancellation = new();
    private bool checking;
    internal string? Executable { get; private set; }

    internal StartupForm()
    {
        Text = "Rirogan";
        if (emblem != null) Icon = emblem;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(460, 304);
        MaximizeBox = MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(16, 21, 36);
        ForeColor = Color.FromArgb(230, 234, 250);
        Font = new Font("Segoe UI", 10);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(24) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 77));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 15));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));

        var badge = new Panel { Dock = DockStyle.Fill };
        badge.Paint += (_, e) =>
        {
            if (emblem != null) e.Graphics.DrawIcon(emblem, new Rectangle((badge.Width - 64) / 2, 5, 64, 64));
        };
        layout.Controls.Add(badge, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "Rirogan",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 19, FontStyle.Bold)
        }, 0, 1);
        layout.Controls.Add(message, 0, 2);
        layout.Controls.Add(progress, 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        foreach (var button in new[] { exit, retry })
        {
            button.BackColor = Color.FromArgb(36, 48, 73);
            button.FlatStyle = FlatStyle.Flat;
            buttons.Controls.Add(button);
        }
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);
        exit.Click += (_, _) => Close();
        retry.Click += async (_, _) => await CheckAsync();
        Shown += async (_, _) => await CheckAsync();
        FormClosing += (_, e) =>
        {
            if (!checking) return;
            cancellation.Cancel();
            if (e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            exit.Enabled = false;
            message.Text = "Cancelando a atualização…";
        };
    }

    private async Task CheckAsync()
    {
        checking = true;
        var token = cancellation.Token;
        retry.Visible = false;
        progress.Visible = true;
        message.Text = "Verificando atualizações do Rirogan…";
        using var updater = new ReleaseUpdater();
        var status = new Progress<string>(text =>
        {
            if (checking && !token.IsCancellationRequested) message.Text = text;
        });
        try
        {
            Executable = await Task.Run(() => updater.PrepareAsync(status, token), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                progress.Visible = false;
                retry.Visible = true;
                message.Text = ex is OperationCanceledException
                    ? "A conexão expirou. Tente verificar a atualização novamente."
                    : ex.Message;
            }
        }
        finally
        {
            checking = false;
        }

        if (token.IsCancellationRequested) Close();
        else if (Executable != null) DialogResult = DialogResult.OK;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            emblem?.Dispose();
            cancellation.Dispose();
        }
    }
}
