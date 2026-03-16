using System;
using System.IO;
using Microsoft.Win32;

namespace DeskManager.Services;

/// <summary>
/// Service zum Speichern und Wiederherstellen von Desktop-Icon-Positionen
/// Nutzt Windows Registry zur Verwaltung
/// </summary>
public class DesktopPositionService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\Shell\Bags\1\Desktop";

    /// <summary>
    /// Speichert die Position eines Icons auf dem Desktop in die Registry
    /// </summary>
    public bool SavePosition(string filePath, double x, double y)
    {
        try
        {
            if (x == 0 && y == 0)
            {
                return false; // Ungültige Position
            }

            var fileName = Path.GetFileName(filePath);
            
            using (var regKey = Registry.CurrentUser.OpenSubKey(RegistryPath, true))
            {
                if (regKey == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Registry-Schlüssel nicht gefunden: {RegistryPath}");
                    return false;
                }

                // Speichere Position als encoded Wert
                // Format: int32 X, int32 Y (little-endian)
                var positionBytes = new byte[8];
                BitConverter.GetBytes((int)x).CopyTo(positionBytes, 0);
                BitConverter.GetBytes((int)y).CopyTo(positionBytes, 4);

                regKey.SetValue($"{fileName}:Position", positionBytes, RegistryValueKind.Binary);
                
                System.Diagnostics.Debug.WriteLine($"Position in Registry gespeichert: {fileName} = ({x}, {y})");
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fehler beim Speichern der Position: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Liest die Position eines Icons aus der Registry
    /// </summary>
    public (int x, int y) LoadPosition(string filePath)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            
            using (var regKey = Registry.CurrentUser.OpenSubKey(RegistryPath))
            {
                if (regKey == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Registry-Schlüssel nicht gefunden: {RegistryPath}");
                    return (0, 0);
                }

                var data = regKey.GetValue($"{fileName}:Position") as byte[];
                if (data != null && data.Length >= 8)
                {
                    var x = BitConverter.ToInt32(data, 0);
                    var y = BitConverter.ToInt32(data, 4);
                    
                    System.Diagnostics.Debug.WriteLine($"Position aus Registry gelesen: {fileName} = ({x}, {y})");
                    return (x, y);
                }
            }

            return (0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fehler beim Lesen der Position: {ex.Message}");
            return (0, 0);
        }
    }

    /// <summary>
    /// Löscht die Position eines Icons aus der Registry
    /// </summary>
    public void DeletePosition(string filePath)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            
            using (var regKey = Registry.CurrentUser.OpenSubKey(RegistryPath, true))
            {
                if (regKey != null)
                {
                    regKey.DeleteValue($"{fileName}:Position", false);
                    System.Diagnostics.Debug.WriteLine($"Position gelöscht: {fileName}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fehler beim Löschen der Position: {ex.Message}");
        }
    }
}
