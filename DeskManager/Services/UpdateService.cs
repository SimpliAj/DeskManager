using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using WPFMessageBox = System.Windows.MessageBox;

namespace DeskManager.Services;

/// <summary>
/// GitHub-basierter Update-Service für DeskManager
/// Prüft auf neue Releases auf GitHub und bietet Updates an
/// </summary>
public class UpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/SimpliAj/DeskManager/releases";
    private static readonly HttpClient _client = new();
    private bool _isCheckingForUpdates = false;
    private Version? _currentVersion;

    public UpdateService()
    {
        _client.DefaultRequestHeaders.Add("User-Agent", "DeskManager-Updater");
    }

    /// <summary>
    /// Initialisiert den Update-Service beim App-Start
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            await CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update-Service Init Fehler: {ex.Message}");
        }
    }

    /// <summary>
    /// Prüft auf verfügbare Updates von GitHub
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        if (_isCheckingForUpdates)
            return;

        _isCheckingForUpdates = true;

        try
        {
            var response = await _client.GetAsync(GitHubApiUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var latestRelease = root[0];
                var latestVersionStr = latestRelease.GetProperty("tag_name").GetString()?.TrimStart('v');
                var downloadUrl = latestRelease.GetProperty("html_url").GetString();
                var releaseNotes = latestRelease.GetProperty("body").GetString() ?? "";

                if (Version.TryParse(latestVersionStr, out var latestVersion))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Aktuelle Version: {_currentVersion}, " +
                        $"Verfügbar: {latestVersion}"
                    );

                    if (latestVersion > _currentVersion)
                    {
                        ShowUpdateDialog(latestVersion, downloadUrl, releaseNotes);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update-Check Fehler: {ex.Message}");
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }

    /// <summary>
    /// Zeigt Update-Dialog für Benutzer
    /// </summary>
    private void ShowUpdateDialog(Version newVersion, string? downloadUrl, string releaseNotes)
    {
        var message = $"Neue Version verfügbar: {newVersion}\n\n";

        if (!string.IsNullOrWhiteSpace(releaseNotes))
        {
            // Zeige nur die ersten 200 Zeichen der Release Notes
            var notes = releaseNotes.Length > 200 
                ? releaseNotes.Substring(0, 200) + "..." 
                : releaseNotes;
            message += $"Änderungen:\n{notes}\n\n";
        }

        message += "Möchtest du die Update jetzt herunterladen?";

        var result = WPFMessageBox.Show(
            message,
            "DeskManager Update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information
        );

        if (result == MessageBoxResult.Yes && !string.IsNullOrEmpty(downloadUrl))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = downloadUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show(
                    $"Fehler beim Öffnen des Links:\n{ex.Message}",
                    "Link-Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
    }

    /// <summary>
    /// Cleanup
    /// </summary>
    public void Dispose()
    {
        // HttpClient wird nicht disposed, da es static ist
    }
}
