using System.IO;
using System.Windows;
using System.Xml;
using DeskManager.Helpers;
using DeskManager.Models;
using DeskManager.Windows;

namespace DeskManager.Services;

public class GridManager
{
    private readonly ConfigManager _configManager = new();
    private readonly FileStorageService _fileStorage = new();
    private readonly List<GridWindow> _windows = [];
    private AppConfig _config = new();
    private bool _allVisible = true;
    private bool _desktopIconsVisible = true;  // Track desktop icons visibility state

    public event Action? SpacesChanged;

    public IReadOnlyList<SpaceData> Spaces => _config.Spaces;
    public string ActiveSpaceId => _config.ActiveSpaceId;
    public AppConfig Config => _config;

    // ─── Init ───────────────────────────────────────────────────────────────

    public void LoadFromConfig(bool isFirstLaunch = false)
    {
        _config = isFirstLaunch ? _configManager.CreateDefaultForFirstLaunch() : _configManager.Load();
        ThemeService.Apply(_config.Theme);

        if (_config.Spaces.Count == 0)
        {
            var s = new SpaceData { Name = "Default" };
            _config.Spaces.Add(s);
        }
        if (!_config.Spaces.Any(s => s.Id == _config.ActiveSpaceId))
            _config.ActiveSpaceId = _config.Spaces[0].Id;

        // Clean up non-existent items from config before loading
        int removedCount = CleanupNonExistentItems();

        // Move grid files to storage (re-hide them) on startup
        int hiddenCount = HideAllGridItemsOnStartup();

        // Single save if anything changed
        if (removedCount > 0 || hiddenCount > 0)
            SaveConfig();

        foreach (var grid in ActiveSpace.Grids)
            SpawnWindow(grid);

        // Restore tab groupings after all windows are spawned
        RestoreGroupings();
    }

    /// Restore parent-child groupings and hide child windows
    private void RestoreGroupings()
    {
        foreach (var space in _config.Spaces)
        {
            foreach (var grid in space.Grids)
            {
                // If this grid has child grids, hide them and refresh parent tabs
                if (grid.ChildGridIds.Count > 0)
                {
                    // Hide all child windows
                    foreach (var childId in grid.ChildGridIds)
                    {
                        var childWindow = GetWindowForGrid(childId);
                        if (childWindow != null)
                            childWindow.Hide();
                    }

                    // Tell parent to refresh tabs
                    var parentWindow = GetWindowForGrid(grid.Id);
                    if (parentWindow != null)
                        parentWindow.RefreshTabs();
                }
            }
        }
    }

    public void SaveConfig()
    {
        foreach (var w in _windows)
            w.FlushPositionToData();
        _configManager.Save(_config);
    }

    /// Move all grid files to storage (hide them) on startup
    private int HideAllGridItemsOnStartup()
    {
        System.Diagnostics.Debug.WriteLine("HideAllGridItemsOnStartup called");
        int count = 0;
        
        foreach (var space in _config.Spaces)
        {
            count += HideGridItemsOnStartup(space.Grids);
        }
        
        System.Diagnostics.Debug.WriteLine($"Hid {count} items on startup");
        return count;
    }

    private int HideGridItemsOnStartup(List<GridData> grids)
    {
        int count = 0;
        foreach (var grid in grids)
        {
            foreach (var item in grid.Items)
            {
                // Skip Shell objects
                if (item.Path.StartsWith("::"))
                    continue;

                // If OriginalPath exists, file is currently on desktop - move it to storage
                if (!string.IsNullOrEmpty(item.OriginalPath) && File.Exists(item.OriginalPath))
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"Moving to storage on startup: {item.OriginalPath}");
                        string newPath = _fileStorage.MoveToStorage(item.OriginalPath);
                        item.Path = newPath;
                        System.Diagnostics.Debug.WriteLine($"  ✅ Now at: {newPath}");
                        count++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ❌ Error moving to storage: {ex.Message}");
                    }
                }
            }
            
            // Recursively hide child grids
            if (grid.ChildGridIds.Count > 0)
            {
                foreach (var childId in grid.ChildGridIds)
                {
                    var childGrid = FindGridById(childId);
                    if (childGrid != null)
                    {
                        count += HideGridItemsOnStartup([childGrid]);
                    }
                }
            }
        }
        return count;
    }

    /// Move all grid files back to their original locations (called on app exit)
    public void RestoreAllFilesFromStorage()
    {
        System.Diagnostics.Debug.WriteLine("RestoreAllFilesFromStorage called");
        int restoredCount = 0;
        
        foreach (var space in _config.Spaces)
        {
            restoredCount += RestoreFilesFromGrids(space.Grids);
        }
        
        System.Diagnostics.Debug.WriteLine($"Restored {restoredCount} files to original locations");
    }

    private int RestoreFilesFromGrids(List<GridData> grids)
    {
        int count = 0;
        foreach (var grid in grids)
        {
            foreach (var item in grid.Items)
            {
                // Skip Shell objects only
                if (item.Path.StartsWith("::"))
                    continue;

                // If OriginalPath exists, move file back from storage
                if (!string.IsNullOrEmpty(item.OriginalPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Restoring: {item.Path} → {item.OriginalPath}");
                    if (_fileStorage.MoveFromStorage(item.Path, item.OriginalPath))
                    {
                        count++;
                    }
                }
            }
            
            // Recursively restore from child grids
            if (grid.ChildGridIds.Count > 0)
            {
                foreach (var childId in grid.ChildGridIds)
                {
                    var childGrid = FindGridById(childId);
                    if (childGrid != null)
                    {
                        count += RestoreFilesFromGrids([childGrid]);
                    }
                }
            }
        }
        return count;
    }

    /// Unhide all files that are in any grid (called on app exit)
    public void UnhideAllGridItems()
    {
        int count = 0;
        foreach (var space in _config.Spaces)
            count += SetGridItemsHiddenState(space.Grids, hidden: false);
        System.Diagnostics.Debug.WriteLine($"Unhid {count} items");
    }

    /// Hide all files that are in any grid
    private void HideAllGridItems()
    {
        int count = 0;
        foreach (var space in _config.Spaces)
            count += SetGridItemsHiddenState(space.Grids, hidden: true);
        System.Diagnostics.Debug.WriteLine($"Hid {count} items");
    }

    private int SetGridItemsHiddenState(List<GridData> grids, bool hidden)
    {
        int count = 0;
        foreach (var grid in grids)
        {
            foreach (var item in grid.Items)
            {
                if (item.Path.StartsWith("::")) continue;
                if (IsWindowsShortcut(item.Path)) continue;
                if (!File.Exists(item.Path) && !Directory.Exists(item.Path)) continue;

                Win32Helper.SetFileHidden(item.Path, hidden);
                count++;
            }

            foreach (var childId in grid.ChildGridIds)
            {
                var childGrid = FindGridById(childId);
                if (childGrid != null)
                    count += SetGridItemsHiddenState([childGrid], hidden);
            }
        }
        return count;
    }

    /// Remove non-existent items from all grids to prevent errors
    private int CleanupNonExistentItems()
    {
        System.Diagnostics.Debug.WriteLine("CleanupNonExistentItems called");
        int removedCount = 0;
        
        foreach (var space in _config.Spaces)
        {
            removedCount += CleanupGridItems(space.Grids);
        }
        
        if (removedCount > 0)
        {
            System.Diagnostics.Debug.WriteLine($"Removed {removedCount} non-existent items from config");
        }
        return removedCount;
    }

    /// Public method to cleanup and unhide safely (called on exit)
    public void CleanupAndUnhideAll()
    {
        System.Diagnostics.Debug.WriteLine("CleanupAndUnhideAll called");
        // First cleanup non-existent items
        int removed = CleanupNonExistentItems();
        if (removed > 0)
        {
            System.Diagnostics.Debug.WriteLine($"Cleaned {removed} items before unhiding");
        }
        // Then unhide what remains (only valid files)
        UnhideAllGridItems();
    }

    /// Check if path is a Windows .lnk shortcut (not .url internet shortcuts)
    private static bool IsWindowsShortcut(string path)
    {
        return path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
    }

    private int CleanupGridItems(List<GridData> grids)
    {
        int count = 0;
        foreach (var grid in grids)
        {
            // DETECT BROKEN ITEMS: If Path == OriginalPath, it means the move to storage failed
            var brokenItems = grid.Items.Where(item =>
                !item.Path.StartsWith("::") &&
                !string.IsNullOrEmpty(item.OriginalPath) &&
                item.Path == item.OriginalPath &&
                !File.Exists(item.Path) &&
                !Directory.Exists(item.Path)
            ).ToList();

            foreach (var item in brokenItems)
            {
                System.Diagnostics.Debug.WriteLine($"❌ BROKEN ITEM (move to storage failed): {item.Name} at {item.Path}");
                grid.Items.Remove(item);
                count++;
            }

            // Remove items that don't exist and aren't shell objects
            // Check both Path (storage location) and OriginalPath (original location)
            var itemsToRemove = grid.Items.Where(item =>
                !item.Path.StartsWith("::") &&
                !File.Exists(item.Path) &&
                !Directory.Exists(item.Path) &&
                !File.Exists(item.OriginalPath ?? "") &&
                !Directory.Exists(item.OriginalPath ?? "")
            ).ToList();

            foreach (var item in itemsToRemove)
            {
                System.Diagnostics.Debug.WriteLine($"Removing non-existent item: {item.Path} (original: {item.OriginalPath})");
                grid.Items.Remove(item);
                count++;
            }

            // Recursively cleanup child grids
            if (grid.ChildGridIds.Count > 0)
            {
                foreach (var childId in grid.ChildGridIds)
                {
                    var childGrid = FindGridById(childId);
                    if (childGrid != null)
                    {
                        count += CleanupGridItems([childGrid]);
                    }
                }
            }
        }
        return count;
    }

    // ─── Theme ──────────────────────────────────────────────────────────────

    public ThemeConfig GetTheme() => _config.Theme;

    public void ApplyTheme(ThemeConfig theme)
    {
        _config.Theme = theme;
        ThemeService.Apply(theme);
        SaveConfig();
    }

    // ─── Grids ─────────────────────────────────────────────────────────────

    public GridWindow CreateGrid()
    {
        var data = new GridData { Title = "New Grid", X = 200, Y = 200, Width = 220, Height = 200 };
        ActiveSpace.Grids.Add(data);
        var w = SpawnWindow(data);
        SaveConfig();
        return w;
    }

    /// Creates a grid at a position drawn by the user on the desktop (WPF DIPs).
    public GridWindow CreateGridAtRect(Rect rect)
    {
        var data = new GridData
        {
            Title  = "New Grid",
            X      = rect.X,
            Y      = rect.Y,
            Width  = Math.Max(130, rect.Width  - 8),
            Height = Math.Max(80,  rect.Height - 12),
        };
        ActiveSpace.Grids.Add(data);
        var w = SpawnWindow(data);
        w.BeginEditTitlePublic(); // immediately rename
        SaveConfig();
        return w;
    }

    /// Returns physical-pixel bounds of all open grid windows (for hit-test in draw service).
    public IEnumerable<Win32Helper.RECT> GetGridBounds()
    {
        foreach (var w in _windows)
        {
            Win32Helper.GetWindowRect(w.Hwnd, out var r);
            yield return r;
        }
    }

    public void DeleteGrid(GridWindow window)
    {
        // Restore all items in this grid (and child grids) back to their original locations
        RestoreFilesFromGrids([window.GridData]);

        ActiveSpace.Grids.Remove(window.GridData);
        _windows.Remove(window);
        window.ForceClose();
        SaveConfig();

        Win32Helper.RefreshDesktop();
    }

    public void ToggleAll()
    {
        _allVisible = !_allVisible;
        
        // Only toggle parent grids (not tab children)
        foreach (var w in _windows)
        {
            // Skip if this grid is a child of another grid (tab)
            if (w.GridData.ParentGridId != null)
                continue;
                
            if (_allVisible) w.Show();
            else w.Hide();
        }
        
        // Toggle desktop icons as well
        Win32Helper.ToggleDesktopIcons(_allVisible);
        System.Diagnostics.Debug.WriteLine($"🔄 ToggleAll - Grids AND Desktop Icons: {_allVisible}");
        ShowNotification(_allVisible ? "Grids und Desktop Icons eingeblendet " : "Grids und Desktop Icons ausgeblendet ");
    }

    public void ToggleGrids()
    {
        // Toggle grids AND desktop icons (same as ToggleAll)
        ToggleAll();
    }

    public void ToggleDesktopIcons()
    {
        // Toggle ONLY desktop icons, keep grids visible
        _desktopIconsVisible = !_desktopIconsVisible;
        Win32Helper.ToggleDesktopIcons(_desktopIconsVisible);
        System.Diagnostics.Debug.WriteLine($"🖥️ Desktop Icons toggled - visible: {_desktopIconsVisible}");
        ShowNotification(_desktopIconsVisible ? "Desktop Icons eingeblendet " : "Desktop Icons ausgeblendet ");
    }

    public void ToggleAllCollapse()
    {
        bool anyOpen = _windows.Any(w => !w.IsCollapsed);
        if (anyOpen) CollapseAll();
        else         ExpandAll();
    }

    public void CollapseAll()
    {
        foreach (var w in _windows)
        {
            if (!w.IsCollapsed)
                w.Dispatcher.Invoke(() => w.PublicCollapse());
        }
        SaveConfig();
    }

    public void ExpandAll()
    {
        // Expand all grid windows (opposite of collapse)
        System.Diagnostics.Debug.WriteLine($"🔓 ExpandAll() called - found {_windows.Count} windows");
        
        int expandedCount = 0;
        foreach (var w in _windows)
        {
            System.Diagnostics.Debug.WriteLine($"  • {w.GridData.Title}: IsCollapsed={w.IsCollapsed}");
            // Call Expand method if the window is collapsed
            if (w.IsCollapsed)
            {
                System.Diagnostics.Debug.WriteLine($"    → Calling PublicExpand()");
                try
                {
                    // Ensure UI updates happen on the main thread
                    w.Dispatcher.Invoke(() =>
                    {
                        w.PublicExpand();
                    });
                    expandedCount++;
                    System.Diagnostics.Debug.WriteLine($"    ✅ Expanded successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"    ❌ Error expanding: {ex.Message}");
                }
            }
        }
        
        System.Diagnostics.Debug.WriteLine($"  ✅ Expanded {expandedCount} grids total");
        SaveConfig();
    }
    // ─── Spaces ─────────────────────────────────────────────────────────────

    public SpaceData CreateSpace(string name)
    {
        var space = new SpaceData { Name = name };
        _config.Spaces.Add(space);
        SaveConfig();
        SpacesChanged?.Invoke();
        return space;
    }

    public void RenameSpace(string spaceId, string name)
    {
        var s = _config.Spaces.FirstOrDefault(x => x.Id == spaceId);
        if (s is null) return;
        s.Name = name;
        SaveConfig();
        SpacesChanged?.Invoke();
    }

    public void DeleteSpace(string spaceId)
    {
        if (_config.Spaces.Count <= 1) return;

        if (_config.ActiveSpaceId == spaceId)
            SwitchSpace(_config.Spaces.First(s => s.Id != spaceId).Id);

        _config.Spaces.RemoveAll(s => s.Id == spaceId);
        SaveConfig();
        SpacesChanged?.Invoke();
    }

    public void SwitchSpace(string spaceId)
    {
        if (_config.ActiveSpaceId == spaceId) return;

        foreach (var w in _windows) w.FlushPositionToData();
        foreach (var w in _windows.ToList()) w.ForceClose();
        _windows.Clear();

        _config.ActiveSpaceId = spaceId;

        foreach (var grid in ActiveSpace.Grids)
            SpawnWindow(grid);

        SaveConfig();
        SpacesChanged?.Invoke();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private SpaceData ActiveSpace =>
        _config.Spaces.FirstOrDefault(s => s.Id == _config.ActiveSpaceId)
        ?? _config.Spaces[0];

    private GridWindow SpawnWindow(GridData data)
    {
        var w = new GridWindow(data, this);
        w.Show();
        _windows.Add(w);
        return w;
    }

    // ─── Grid Grouping ──────────────────────────────────────────────────────

    public void GroupGrids(GridData parentGrid, GridData childGrid)
    {
        // Make child a tab of parent
        if (parentGrid.ChildGridIds.Contains(childGrid.Id)) return;
        
        // If child already has a parent, remove it from old parent
        if (childGrid.ParentGridId != null)
        {
            var oldParent = FindGridById(childGrid.ParentGridId);
            if (oldParent != null)
                oldParent.ChildGridIds.Remove(childGrid.Id);
        }

        parentGrid.ChildGridIds.Add(childGrid.Id);
        childGrid.ParentGridId = parentGrid.Id;

        // Hide the child window
        var childWindow = _windows.FirstOrDefault(w => w.GridData.Id == childGrid.Id);
        if (childWindow != null)
            childWindow.Hide();

        // Notify parent window to update tabs and bring to front
        var parentWindow = _windows.FirstOrDefault(w => w.GridData.Id == parentGrid.Id);
        if (parentWindow != null)
        {
            parentWindow.RefreshTabs();
            parentWindow.Activate();
            parentWindow.Focus();
        }

        SaveConfig();
    }

    public void UngroupGrid(GridData childGrid)
    {
        if (childGrid.ParentGridId == null) return;

        var parentGrid = FindGridById(childGrid.ParentGridId);
        if (parentGrid != null)
        {
            parentGrid.ChildGridIds.Remove(childGrid.Id);
            childGrid.ParentGridId = null;

            // Show the child window again
            var childWindow = _windows.FirstOrDefault(w => w.GridData.Id == childGrid.Id);
            if (childWindow != null)
                childWindow.Show();

            // Update parent tabs
            var parentWindow = _windows.FirstOrDefault(w => w.GridData.Id == parentGrid.Id);
            if (parentWindow != null)
                parentWindow.RefreshTabs();
        }

        SaveConfig();
    }

    public GridData? FindGridById(string gridId)
    {
        foreach (var space in _config.Spaces)
        {
            var grid = FindGridInList(space.Grids, gridId);
            if (grid != null) return grid;
        }
        return null;
    }

    private GridData? FindGridInList(List<GridData> grids, string gridId)
    {
        foreach (var grid in grids)
        {
            if (grid.Id == gridId) return grid;
            // Check recursively in nested grids if needed
        }
        return null;
    }

    public GridWindow? GetWindowForGrid(string gridId)
    {
        return _windows.FirstOrDefault(w => w.GridData.Id == gridId);
    }

    /// Close all open grid windows
    public void CloseAllGridWindows()
    {
        System.Diagnostics.Debug.WriteLine($"Closing {_windows.Count} grid windows...");
        foreach (var window in _windows.ToList())
        {
            window.Close();
        }
        _windows.Clear();
        System.Diagnostics.Debug.WriteLine("✅ All windows closed");
    }

    /// Delete all grids from all spaces
    public void ClearAllGrids()
    {
        System.Diagnostics.Debug.WriteLine("Clearing all grids...");
        int gridCount = 0;
        foreach (var space in _config.Spaces)
        {
            gridCount += space.Grids.Count;
            space.Grids.Clear();
        }
        SaveConfig();
        System.Diagnostics.Debug.WriteLine($"✅ Cleared {gridCount} grids");
    }

    /// Show Windows Toast Notification (only if enabled)
    private void ShowNotification(string message)
    {
        // Check if notifications are enabled
        if (!_config.NotificationsEnabled)
        {
            System.Diagnostics.Debug.WriteLine($"📢 Notification disabled: {message}");
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"📢 Showing notification: {message}");
            
            // Run on background thread
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Get app icon path (relative to installation directory)
                    var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                    var iconUri = new Uri(iconPath, UriKind.Absolute).AbsoluteUri;
                    
                    // Simple PowerShell command to show a toast notification
                    string psCommand = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null

$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$xml.LoadXml(@'
<toast duration='short'>
    <visual>
        <binding template='ToastImageAndText02'>
            <image id='1' src='{iconUri}' />
            <text id='1'>DeskManager</text>
            <text id='2'>{message}</text>
        </binding>
    </visual>
</toast>
'@)

$toast = New-Object Windows.UI.Notifications.ToastNotification($xml)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('DeskManager').Show($toast)
";

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -Command \"{psCommand}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };

                    using (var process = System.Diagnostics.Process.Start(psi))
                    {
                        if (process != null)
                        {
                            string output = process.StandardOutput.ReadToEnd();
                            string error = process.StandardError.ReadToEnd();
                            process.WaitForExit(3000);

                            if (!string.IsNullOrEmpty(error))
                            {
                                System.Diagnostics.Debug.WriteLine($"⚠️ PowerShell Error: {error}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"✅ Notification sent successfully");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Notification error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Notification queue error: {ex.Message}");
        }
    }
}
