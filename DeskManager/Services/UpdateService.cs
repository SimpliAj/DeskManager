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
        
        // Get and debug current version
        try
        {
            _currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            System.Diagnostics.Debug.WriteLine($"📦 UpdateService initialized - Current App Version: {_currentVersion}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error reading assembly version: {ex.Message}");
        }
    }

    /// <summary>
    /// Initialisiert den Update-Service beim App-Start
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            System.Diagnostics.Debug.WriteLine($"📦 UpdateService initialized - Current App Version: {_currentVersion}");
            // Check for updates wird nur durchgeführt wenn der Benutzer den Button drückt
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

            System.Diagnostics.Debug.WriteLine($"GitHub API returned {root.GetArrayLength()} releases");

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                // Find the latest version by parsing all releases and comparing versions
                Version? latestVersion = null;
                JsonElement? latestReleaseElement = null;
                string? latestVersionStr = null;

                for (int i = 0; i < root.GetArrayLength(); i++)
                {
                    var release = root[i];
                    var tagName = release.GetProperty("tag_name").GetString()?.TrimStart('v');
                    
                    System.Diagnostics.Debug.WriteLine($"Release {i}: tag_name={tagName}");

                    if (Version.TryParse(tagName, out var version))
                    {
                        if (latestVersion == null || version > latestVersion)
                        {
                            latestVersion = version;
                            latestReleaseElement = release;
                            latestVersionStr = tagName;
                        }
                    }
                }

                if (latestReleaseElement.HasValue)
                {
                    var downloadUrl = latestReleaseElement.Value.GetProperty("html_url").GetString();
                    var releaseNotes = latestReleaseElement.Value.GetProperty("body").GetString() ?? "";

                    System.Diagnostics.Debug.WriteLine(
                        $"Aktuelle Version: {_currentVersion}, " +
                        $"Verfügbar (latest): {latestVersion}"
                    );

                    if (latestVersion > _currentVersion)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Update available! {latestVersion} > {_currentVersion}");
                        ShowUpdateDialog(latestVersion, downloadUrl, releaseNotes);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ App is up to date. {_currentVersion} >= {latestVersion}");
                        ShowUpToDateDialog(latestVersion);
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
        System.Diagnostics.Debug.WriteLine($"🔍 ShowUpdateDialog called with version: {newVersion}");
        System.Diagnostics.Debug.WriteLine($"   Current version: {_currentVersion}");
        System.Diagnostics.Debug.WriteLine($"   Comparison: {newVersion} > {_currentVersion} = {newVersion > _currentVersion}");
        
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
    /// Zeigt Dialog an, wenn App bereits up-to-date ist
    /// </summary>
    private void ShowUpToDateDialog(Version currentVersion)
    {
        var message = $"✅ Du hast bereits die neueste Version: {currentVersion}\n\n" +
                      "Deine Installation ist aktuell.";

        WPFMessageBox.Show(
            message,
            "DeskManager aktuell",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    /// <summary>
    /// Cleanup
    /// </summary>
    public void Dispose()
    {
        // HttpClient wird nicht disposed, da es static ist
    }
}
