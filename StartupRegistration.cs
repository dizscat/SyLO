using System;
using Microsoft.Win32;

namespace SyLO;

/// <summary>Daftarkan / hapus SyLO dari startup Windows (HKCU\...\Run, hanya untuk user ini).</summary>
public static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "SyLO";

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled && Environment.ProcessPath is { } exe)
                key.SetValue(Name, $"\"{exe}\"");
            else
                key.DeleteValue(Name, false);
        }
        catch { }
    }
}
