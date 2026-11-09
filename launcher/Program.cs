using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RiroganDesktop;

internal static class Program
{
    internal static Version MinimumDesktopVersion { get; } = new(typeof(Program).Assembly.GetName().Version!.ToString(3));

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool autostart = Array.IndexOf(args, "--autostart") >= 0;
        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ReleaseChannel.InstanceName + ".Activate");
        using var mutex = new Mutex(false, ReleaseChannel.InstanceName + ".Launcher");
        bool owns;
        try { owns = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { owns = true; }

        if (!owns)
        {
            if (!autostart)
            {
                AllowSetForegroundWindow(uint.MaxValue);
                activate.Set();
            }
            return;
        }

        try
        {
            // Se o launcher anterior foi encerrado à força, a janela do app pode continuar aberta.
            if (Mutex.TryOpenExisting(ReleaseChannel.InstanceName, out var desktop))
            {
                using (desktop)
                {
                    bool available;
                    try { available = desktop.WaitOne(0); }
                    catch (AbandonedMutexException) { available = true; }
                    if (!available)
                    {
                        if (!autostart)
                        {
                            AllowSetForegroundWindow(uint.MaxValue);
                            activate.Set();
                        }
                        return;
                    }
                    desktop.ReleaseMutex();
                }
            }

            Directory.CreateDirectory(ReleaseUpdater.CacheRoot);
            // Sessões do Windows distintas podem compartilhar o mesmo perfil de usuário.
            using var cacheLock = new FileStream(Path.Combine(ReleaseUpdater.CacheRoot, "launcher.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            while (true)
            {
                string executable;
                using (var startup = new StartupForm())
                using (var timer = new System.Windows.Forms.Timer { Interval = 150 })
                {
                    timer.Tick += (_, _) =>
                    {
                        if (!activate.WaitOne(0)) return;
                        autostart = false;
                        startup.Activate();
                    };
                    timer.Start();
                    if (startup.ShowDialog() != DialogResult.OK || startup.Executable is not string ready) return;
                    executable = ready;
                }

                var start = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(executable)!
                };
                start.ArgumentList.Add("--launcher");
                start.ArgumentList.Add(Application.ExecutablePath);
                if (autostart) start.ArgumentList.Add("--autostart");
                using var app = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o Rirogan Desktop.");
                app.WaitForExit();
                if (app.ExitCode != ReleaseChannel.RestartForUpdate)
                {
                    if (app.ExitCode != 0)
                        MessageBox.Show($"O Rirogan Desktop terminou com o código {app.ExitCode}.", "Rirogan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                autostart = false;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Rirogan Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
