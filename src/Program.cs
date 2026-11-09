using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RiroganDesktop;

internal static class Program
{
    internal static Version AppVersion { get; } = new(typeof(Program).Assembly.GetName().Version!.ToString(3));
    internal static string LauncherPath { get; private set; } = "";

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool autostart = Array.IndexOf(args, "--autostart") >= 0;
        int launcherArg = Array.IndexOf(args, "--launcher");
        if (launcherArg >= 0 && launcherArg + 1 < args.Length &&
            Path.IsPathFullyQualified(args[launcherArg + 1]) && File.Exists(args[launcherArg + 1]))
            LauncherPath = args[launcherArg + 1];
        else
        {
            var launcher = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Rirogan.exe"));
            if (File.Exists(launcher))
            {
                try
                {
                    var start = new ProcessStartInfo(launcher) { UseShellExecute = false };
                    if (autostart) start.ArgumentList.Add("--autostart");
                    Process.Start(start)?.Dispose();
                    return 0;
                }
                catch (Exception) { /* Informa como abrir o aplicativo abaixo. */ }
            }
            MessageBox.Show("Abra o Rirogan pelo atalho do launcher.", "Rirogan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        using var instance = new Mutex(false, ReleaseChannel.InstanceName);
        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ReleaseChannel.InstanceName + ".Activate");
        bool ownsInstance;
        try { ownsInstance = instance.WaitOne(0); }
        catch (AbandonedMutexException) { ownsInstance = true; }
        if (!ownsInstance)
        {
            if (!autostart)
            {
                AllowSetForegroundWindow(uint.MaxValue);
                activate.Set();
            }
            return 0;
        }

        try
        {
            using var main = new MainForm(autostart);
            using var timer = new System.Windows.Forms.Timer { Interval = 150 };
            timer.Tick += (_, _) =>
            {
                if (activate.WaitOne(0)) main.RestoreFromTray();
            };
            timer.Start();
            Application.Run(main);
            return main.RestartForUpdate ? ReleaseChannel.RestartForUpdate : 0;
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }
}
