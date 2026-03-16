using Microsoft.Win32;
using System;

namespace DeskManager.Helpers;

public static class AutostartHelper
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "DeskManager";

    /// Check if app is set to autostart
    public static bool IsAutostartEnabled()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
            {
                return key?.GetValue(AppName) != null;
            }
        }
        catch { return false; }
    }

    /// Enable autostart by creating registry entry
    public static void EnableAutostart()
    {
        try
        {
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath)) return;

            using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true))
            {
                if (key != null)
                {
                    key.SetValue(AppName, exePath, RegistryValueKind.String);
                    System.Diagnostics.Debug.WriteLine($"✅ Autostart enabled: {exePath}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error enabling autostart: {ex.Message}");
        }
    }

    /// Disable autostart by removing registry entry
    public static void DisableAutostart()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true))
            {
                if (key != null)
                {
                    key.DeleteValue(AppName, false);
                    System.Diagnostics.Debug.WriteLine($"❌ Autostart disabled");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error disabling autostart: {ex.Message}");
        }
    }
}
