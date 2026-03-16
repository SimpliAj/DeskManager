using System.Drawing;
using System.IO;
using System.Windows;
using DeskManager.Models;
using DeskManager.Services;
using DeskManager.Windows;
using WinForms    = System.Windows.Forms;
using Application = System.Windows.Application;

namespace DeskManager;

public partial class App : Application
{
    private GridManager _gridManager = null!;
    private WinForms.NotifyIcon _trayIcon = null!;
    private WinForms.ToolStripMenuItem _spacesMenu = null!;
    private DesktopDrawService? _drawService;
    private DrawingOverlay? _drawOverlay;
    private UpdateService _updateService = null!;
    private bool _openSettingsOnStartup = false;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check for command-line arguments
        _openSettingsOnStartup = e.Args.Contains("--settings") || e.Args.Contains("/settings");
        bool isFirstLaunch = e.Args.Contains("--first-launch") || e.Args.Contains("/first-launch");

        _gridManager = new GridManager();
        _gridManager.LoadFromConfig(isFirstLaunch);
        _gridManager.SpacesChanged += RebuildSpacesMenu;

        // Starten des Update-Services
        _updateService = new UpdateService();
        await _updateService.InitializeAsync();

        BuildTrayIcon();
        StartDrawService();

        // Open Settings window if requested (first launch or --settings flag)
        if (_openSettingsOnStartup || isFirstLaunch)
        {
            Dispatcher.Invoke(() => OpenSettings());
        }
        
        // Register for session ending event (PC shutdown/logout)
        Current.SessionEnding += Current_SessionEnding;
    }

    // ─── Desktop Draw Service ────────────────────────────────────────────────

    private void StartDrawService()
    {
        _drawOverlay = new DrawingOverlay();

        _drawService = new DesktopDrawService(_gridManager.GetGridBounds);
        _drawService.GridRequested += rect =>
        {
            var menu = new WinForms.ContextMenuStrip();

            var createItem = new WinForms.ToolStripMenuItem("Grid hier erstellen");
            createItem.Click += (_, _) => _gridManager.CreateGridAtRect(rect);
            menu.Items.Add(createItem);

            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(new WinForms.ToolStripMenuItem("Abbrechen"));

            menu.Show(WinForms.Cursor.Position);
        };

        // Double-click to toggle all grids visibility
        _drawService.DesktopDoubleClicked += () => _gridManager.ToggleGrids();
        _drawService.DesktopDoubleRightClicked += () => _gridManager.ToggleDesktopIcons();

        _drawService.Start(_drawOverlay);
    }

    // ─── Tray Icon ──────────────────────────────────────────────────────────

    private void BuildTrayIcon()
    {
        // Load the app icon from the executable
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
        var icon = File.Exists(iconPath) 
            ? new System.Drawing.Icon(iconPath) 
            : SystemIcons.Application;

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon    = icon,
            Visible = true,
            Text    = "DeskManager – Desktop Organizer"
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Neues Grid",           null, (_, _) => _gridManager.CreateGrid());
        menu.Items.Add("Alle ein-/ausblenden",  null, (_, _) => _gridManager.ToggleAll());
        menu.Items.Add(new WinForms.ToolStripSeparator());

        _spacesMenu = new WinForms.ToolStripMenuItem("Spaces");
        menu.Items.Add(_spacesMenu);
        RebuildSpacesMenu();

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Einstellungen", null, (_, _) => OpenSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) =>
        {
            _gridManager.SaveConfig();
            // Files are restored on app exit via OnExit handler
            _trayIcon.Visible = false;
            Shutdown();
        });

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick     += (_, _) => _gridManager.ToggleAll();
    }

    private void RebuildSpacesMenu()
    {
        Dispatcher.Invoke(() =>
        {
            _spacesMenu.DropDownItems.Clear();

            foreach (var space in _gridManager.Spaces)
            {
                var item    = new WinForms.ToolStripMenuItem(space.Name);
                item.Checked = space.Id == _gridManager.ActiveSpaceId;
                var id = space.Id;
                item.Click += (_, _) => _gridManager.SwitchSpace(id);
                _spacesMenu.DropDownItems.Add(item);
            }

            _spacesMenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
            var addItem = new WinForms.ToolStripMenuItem("Neuer Space…");
            addItem.Click += (_, _) => OpenSettings();
            _spacesMenu.DropDownItems.Add(addItem);
        });
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(_gridManager);
        win.Show();
    }

    // ─── Shutdown ───────────────────────────────────────────────────────────

    private void Current_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        // Called when Windows is shutting down, user is logging off, or session is ending
        System.Diagnostics.Debug.WriteLine($"=== SessionEnding called (Reason: {e.ReasonSessionEnding}) ===");
        
        try
        {
            System.Diagnostics.Debug.WriteLine("Performing emergency cleanup on session end...");
            
            // Restore all grid files to their original locations (priority!)
            System.Diagnostics.Debug.WriteLine("Restoring files from storage...");
            _gridManager?.RestoreAllFilesFromStorage();
            
            // Save config
            System.Diagnostics.Debug.WriteLine("Saving config...");
            _gridManager?.SaveConfig();
            
            System.Diagnostics.Debug.WriteLine("✅ Emergency cleanup complete");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error during session ending cleanup: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("=== App OnExit called ===");
        
        _drawService?.Dispose();
        _updateService?.Dispose();
        
        // Restore all grid files to their original locations
        System.Diagnostics.Debug.WriteLine("Restoring files from storage to original locations...");
        _gridManager?.RestoreAllFilesFromStorage();
        
        System.Diagnostics.Debug.WriteLine("Saving config...");
        _gridManager?.SaveConfig();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        
        System.Diagnostics.Debug.WriteLine("=== App exit complete ===");
        base.OnExit(e);
    }
}
