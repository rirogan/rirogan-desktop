using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RiroganDesktop;

// Hook de teclado exclusivamente para o botão Push-to-Talk configurado.
// Não registra teclas digitadas, não captura texto, não bloqueia eventos de outros apps.
internal sealed class GlobalPushToTalk : IDisposable
{
    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private readonly HookProc callback;
    private IntPtr hook;
    private bool held;
    public Keys Hotkey { get; set; } = Keys.F8;
    public bool Enabled { get; set; }
    public event Action<bool>? Changed;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string? lpModuleName);

    public GlobalPushToTalk()
    {
        callback = OnHook;
    }
    public bool Start()
    {
        if (hook != IntPtr.Zero) return true;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, callback, GetModuleHandle(module?.ModuleName), 0);
        return hook != IntPtr.Zero;
    }
    private IntPtr OnHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && Enabled)
        {
            int message = unchecked((int)wParam.ToInt64());
            if (message is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP)
            {
                var key = (Keys)Marshal.ReadInt32(lParam);
                if (key == Hotkey)
                {
                    bool pressed = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
                    if (held != pressed) { held = pressed; Changed?.Invoke(held); }
                }
            }
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }
    public void Release()
    {
        if (!held) return;
        held = false;
        Changed?.Invoke(false);
    }
    public void Dispose()
    {
        Enabled = false;
        Release();
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    }
}
