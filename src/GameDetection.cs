using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace RiroganDesktop;

internal static class GameDetection
{
    // Detecção local e opt-in. Só verifica o nome dos executáveis da lista.
    // Não coleta títulos de janelas, caminhos, comandos, processos sensíveis ou telemetria.
    private static readonly Dictionary<string,string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Minecraft.Windows"] = "Minecraft",
        ["VALORANT-Win64-Shipping"] = "VALORANT", ["FortniteClient-Win64-Shipping"] = "Fortnite",
        ["cs2"] = "Counter-Strike 2", ["GTA5"] = "GTA V", ["GTA5_Enhanced"] = "GTA V Enhanced",
        ["RobloxPlayerBeta"] = "Roblox", ["League of Legends"] = "League of Legends",
        ["TheForest"] = "The Forest", ["SonsOfTheForest"] = "Sons of the Forest",
        ["eldenring"] = "Elden Ring", ["r5apex"] = "Apex Legends", ["Overwatch"] = "Overwatch",
        ["RocketLeague"] = "Rocket League", ["Terraria"] = "Terraria", ["Stardew Valley"] = "Stardew Valley",
        ["Among Us"] = "Among Us", ["DeadByDaylight-Win64-Shipping"] = "Dead by Daylight",
        ["Palworld-Win64-Shipping"] = "Palworld", ["RustClient"] = "Rust", ["RDR2"] = "Red Dead Redemption 2"
    };
    public static string? FindActive()
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); } catch { return null; }
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (Known.TryGetValue(process.ProcessName, out var label)) return label;
                }
                catch { }
            }
            return null;
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }
}
