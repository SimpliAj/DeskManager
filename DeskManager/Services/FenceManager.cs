using System.Windows;
using DeskManager.Helpers;
using DeskManager.Models;
using DeskManager.Windows;

namespace DeskManager.Services;

public class FenceManager
{
    private readonly ConfigManager _configManager = new();
    private readonly List<FenceWindow> _windows = [];
    private AppConfig _config = new();
    private bool _allVisible = true;

    public event Action? SpacesChanged;

    public IReadOnlyList<SpaceData> Spaces => _config.Spaces;
    public string ActiveSpaceId => _config.ActiveSpaceId;

    // ─── Init ───────────────────────────────────────────────────────────────

    public void LoadFromConfig()
    {
        _config = _configManager.Load();
        ThemeService.Apply(_config.Theme);

        if (_config.Spaces.Count == 0)
        {
            var s = new SpaceData { Name = "Default" };
            _config.Spaces.Add(s);
        }
        if (!_config.Spaces.Any(s => s.Id == _config.ActiveSpaceId))
            _config.ActiveSpaceId = _config.Spaces[0].Id;

        foreach (var fence in ActiveSpace.Fences)
            SpawnWindow(fence);
    }

    public void SaveConfig()
    {
        foreach (var w in _windows)
            w.FlushPositionToData();
        _configManager.Save(_config);
    }

    // ─── Theme ──────────────────────────────────────────────────────────────

    public ThemeConfig GetTheme() => _config.Theme;

    public void ApplyTheme(ThemeConfig theme)
    {
        _config.Theme = theme;
        ThemeService.Apply(theme);
        SaveConfig();
    }

    // ─── Fences ─────────────────────────────────────────────────────────────

    public FenceWindow CreateFence()
    {
        var data = new FenceData { Title = "New Fence", X = 200, Y = 200, Width = 220, Height = 200 };
        ActiveSpace.Fences.Add(data);
        var w = SpawnWindow(data);
        SaveConfig();
        return w;
    }

    /// Creates a fence at a position drawn by the user on the desktop (WPF DIPs).
    public FenceWindow CreateFenceAtRect(Rect rect)
    {
        var data = new FenceData
        {
            Title  = "New Fence",
            X      = rect.X,
            Y      = rect.Y,
            Width  = Math.Max(130, rect.Width  - 8),
            Height = Math.Max(80,  rect.Height - 12),
        };
        ActiveSpace.Fences.Add(data);
        var w = SpawnWindow(data);
        w.BeginEditTitlePublic(); // immediately rename
        SaveConfig();
        return w;
    }

    /// Returns physical-pixel bounds of all open fence windows (for hit-test in draw service).
    public IEnumerable<Win32Helper.RECT> GetFenceBounds()
    {
        foreach (var w in _windows)
        {
            Win32Helper.GetWindowRect(w.Hwnd, out var r);
            yield return r;
        }
    }

    public void DeleteFence(FenceWindow window)
    {
        ActiveSpace.Fences.Remove(window.FenceData);
        _windows.Remove(window);
        window.ForceClose();
        SaveConfig();
    }

    public void ToggleAll()
    {
        _allVisible = !_allVisible;
        foreach (var w in _windows)
        {
            if (_allVisible) w.Show();
            else             w.Hide();
        }
        
        // Toggle desktop icons as well
        Win32Helper.ToggleDesktopIcons(_allVisible);
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

        foreach (var fence in ActiveSpace.Fences)
            SpawnWindow(fence);

        SaveConfig();
        SpacesChanged?.Invoke();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private SpaceData ActiveSpace =>
        _config.Spaces.FirstOrDefault(s => s.Id == _config.ActiveSpaceId)
        ?? _config.Spaces[0];

    private FenceWindow SpawnWindow(FenceData data)
    {
        var w = new FenceWindow(data, this);
        w.Show();
        _windows.Add(w);
        return w;
    }
}
