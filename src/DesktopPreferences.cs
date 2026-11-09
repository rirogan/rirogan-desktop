using System;
using Microsoft.Win32;

namespace RiroganDesktop;

internal sealed class DesktopPreferences
{
    private const string Key = @"Software\Rirogan\Desktop";
    public bool PushToTalk { get; set; }
    public bool GlobalShortcuts { get; set; } = true;
    public bool GameDetection { get; set; }
    public bool DoNotDisturb { get; set; }
    public bool HidePreviews { get; set; }
    public bool EcoMode { get; set; } = true;
    public bool MuteWhileFocused { get; set; } = true;
    public bool? LegacyNoiseSuppression { get; private set; }
    public bool StreamerMode { get; set; }
    public bool AllowMicrophone { get; set; } = true;
    public bool AllowCamera { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool DownloadAlerts { get; set; } = true;
    public bool NotifyLargeFiles { get; set; } = true;
    public string DownloadFolder { get; set; } = "";
    public int MicrophoneKey { get; set; } = 0x78; // F9
    public int DeafenKey { get; set; } = 0x79; // F10
    public int InterfaceScale { get; set; } = 100;
    public int PushKey { get; set; } = 0x77; // F8

    public static DesktopPreferences Read()
    {
        var p = new DesktopPreferences();
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            if (k is null) return p;
            bool Get(string name, bool fallback) => k.GetValue(name) is int v ? v != 0 : fallback;
            p.PushToTalk = Get("PushToTalk", false);
            p.GlobalShortcuts = Get("GlobalShortcuts", true);
            p.GameDetection = Get("GameDetection", false);
            p.DoNotDisturb = Get("DoNotDisturb", false);
            p.HidePreviews = Get("HidePreviews", false);
            p.EcoMode = Get("EcoMode", true);
            p.MuteWhileFocused = Get("MuteWhileFocused", true);
            if (k.GetValue("NoiseSuppression") is int noise) p.LegacyNoiseSuppression = noise != 0;
            p.StreamerMode = Get("StreamerMode", false);
            p.AllowMicrophone = Get("AllowMicrophone", true);
            p.AllowCamera = Get("AllowCamera", true);
            p.StartMinimized = Get("StartMinimized", false);
            p.DownloadAlerts = Get("DownloadAlerts", true);
            p.NotifyLargeFiles = Get("NotifyLargeFiles", true);
            p.DownloadFolder = k.GetValue("DownloadFolder") as string ?? "";
            p.MicrophoneKey = k.GetValue("MicrophoneKey") is int mk && mk >= 0x70 && mk <= 0x7B ? mk : 0x78;
            p.DeafenKey = k.GetValue("DeafenKey") is int dk && dk >= 0x70 && dk <= 0x7B ? dk : 0x79;
            p.InterfaceScale = k.GetValue("InterfaceScale") is int scale && scale >= 80 && scale <= 150 ? scale : 100;
            p.PushKey = k.GetValue("PushKey") is int n && n >= 0x75 && n <= 0x7B ? n : 0x77;
        }
        catch { /* configuração do usuário é opcional */ }
        return p;
    }
    public void Save()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (k is null) return;
            void Put(string n, bool v) => k.SetValue(n, v ? 1 : 0, RegistryValueKind.DWord);
            Put("PushToTalk", PushToTalk);
            Put("GlobalShortcuts", GlobalShortcuts);
            Put("GameDetection", GameDetection);
            Put("DoNotDisturb", DoNotDisturb);
            Put("HidePreviews", HidePreviews);
            Put("EcoMode", EcoMode);
            Put("MuteWhileFocused", MuteWhileFocused);
            Put("StreamerMode", StreamerMode);
            Put("AllowMicrophone", AllowMicrophone);
            Put("AllowCamera", AllowCamera);
            Put("StartMinimized", StartMinimized);
            Put("DownloadAlerts", DownloadAlerts);
            Put("NotifyLargeFiles", NotifyLargeFiles);
            k.SetValue("DownloadFolder", DownloadFolder ?? "", RegistryValueKind.String);
            k.SetValue("MicrophoneKey", MicrophoneKey, RegistryValueKind.DWord);
            k.SetValue("DeafenKey", DeafenKey, RegistryValueKind.DWord);
            k.SetValue("InterfaceScale", InterfaceScale, RegistryValueKind.DWord);
            k.SetValue("PushKey", PushKey, RegistryValueKind.DWord);
        }
        catch { /* configurações não devem bloquear aplicativo */ }
    }
}
