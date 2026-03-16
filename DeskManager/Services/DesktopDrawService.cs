using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using DeskManager.Helpers;
using DeskManager.Windows;
using WinApp = System.Windows.Application;

namespace DeskManager.Services;

/// Listens for click-drag on the empty desktop via WH_MOUSE_LL.
/// When the user draws a rectangle large enough, fires GridRequested.
public class DesktopDrawService : IDisposable
{
    // ─── Win32 ──────────────────────────────────────────────────────────────

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public int mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x, y; }

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc fn, IntPtr hMod, uint tid);
    [DllImport("user32.dll")] static extern bool   UnhookWindowsHookEx(IntPtr hk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hk, int n, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern short GetKeyState(int vKey);

    private const int VK_CONTROL = 0x11;

    private const int WH_MOUSE_LL   = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MOUSEMOVE   = 0x0200;
    private const int WM_LBUTTONUP   = 0x0202;
    private const int WM_RBUTTONUP   = 0x0205;
    private const int MIN_DRAG_PX    = 60;

    // ─── State ──────────────────────────────────────────────────────────────

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelMouseProc? _proc; // must be field to prevent GC
    private bool _dragging;
    private POINT _startPt;
    private DrawingOverlay? _overlay;

    // Double-click detection
    private DateTime _lastClickTime = DateTime.MinValue;
    private POINT _lastClickPt;
    private DateTime _lastRightClickTime = DateTime.MinValue;
    private POINT _lastRightClickPt;
    private const int DOUBLE_CLICK_INTERVAL_MS = 500;
    private const int DOUBLE_CLICK_MAX_DISTANCE = 5;

    /// Provides current grid bounds (physical pixels) for hit-testing
    private readonly Func<IEnumerable<Win32Helper.RECT>> _getGridBounds;

    public event Action<Rect>? GridRequested;
    public event Action? DesktopDoubleClicked;  // Ctrl+Left-Double-Click (toggle grids)
    public event Action? DesktopDoubleRightClicked;  // Ctrl+Right-Double-Click (toggle desktop icons)

    // ─── Public ─────────────────────────────────────────────────────────────

    public DesktopDrawService(Func<IEnumerable<Win32Helper.RECT>> getGridBounds)
    {
        _getGridBounds = getGridBounds;
    }

    public void Start(DrawingOverlay overlay)
    {
        _overlay = overlay;
        _proc    = HookCallback;

        using var p = System.Diagnostics.Process.GetCurrentProcess();
        using var m = p.MainModule!;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(m.ModuleName), 0);
    }

    // ─── Hook ───────────────────────────────────────────────────────────────

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int msg  = (int)wParam;

            if (msg == WM_LBUTTONDOWN && IsDesktopArea(info.pt))
            {
                // Check if Ctrl is held for drag-to-create-grid
                bool ctrlPressed = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                
                // Check for double-click
                var now = DateTime.UtcNow;
                var timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;
                var distanceFromLastClick = Math.Sqrt(
                    Math.Pow(info.pt.x - _lastClickPt.x, 2) + 
                    Math.Pow(info.pt.y - _lastClickPt.y, 2));

                if (timeSinceLastClick <= DOUBLE_CLICK_INTERVAL_MS && 
                    distanceFromLastClick <= DOUBLE_CLICK_MAX_DISTANCE)
                {
                    // Double-click detected - check if Ctrl is held
                    System.Diagnostics.Debug.WriteLine($"🖱️ Desktop Double-Click detected at ({info.pt.x}, {info.pt.y})");
                    _lastClickTime = DateTime.MinValue; // Reset to avoid triple-click
                    
                    // Only toggle if Ctrl is held
                    if (ctrlPressed)
                    {
                        System.Diagnostics.Debug.WriteLine($"  + Ctrl+Left held - toggling grids");
                        WinApp.Current.Dispatcher.BeginInvoke(() => DesktopDoubleClicked?.Invoke());
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"  (no Ctrl - ignoring toggle)");
                    }
                }
                else if (ctrlPressed)
                {
                    // Ctrl+Click detected - start drag to create grid
                    System.Diagnostics.Debug.WriteLine($"🔲 Ctrl+Left-Click detected - starting grid creation");
                    _lastClickTime = now;
                    _lastClickPt = info.pt;
                    _dragging = true;
                    _startPt  = info.pt;
                    var p = ToDrawingPoint(info.pt);
                    WinApp.Current.Dispatcher.BeginInvoke(() => _overlay?.BeginDraw(p));
                }
                else
                {
                    // Regular click - could be first part of double-click
                    System.Diagnostics.Debug.WriteLine($"🖱️ Single left-click at ({info.pt.x}, {info.pt.y}) - tracking for double-click");
                    _lastClickTime = now;
                    _lastClickPt = info.pt;
                }
            }
            else if (msg == WM_RBUTTONDOWN && IsDesktopArea(info.pt))
            {
                // Check if Ctrl is held for right-click toggle
                bool ctrlPressed = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                
                System.Diagnostics.Debug.WriteLine($"🖱️ Right-Click at ({info.pt.x}, {info.pt.y}) - Ctrl: {ctrlPressed}");
                
                if (!ctrlPressed) return CallNextHookEx(_hookId, nCode, wParam, lParam);

                // Check for double-click
                var now = DateTime.UtcNow;
                var timeSinceLastClick = (now - _lastRightClickTime).TotalMilliseconds;
                var distanceFromLastClick = Math.Sqrt(
                    Math.Pow(info.pt.x - _lastRightClickPt.x, 2) + 
                    Math.Pow(info.pt.y - _lastRightClickPt.y, 2));

                if (timeSinceLastClick <= DOUBLE_CLICK_INTERVAL_MS && 
                    distanceFromLastClick <= DOUBLE_CLICK_MAX_DISTANCE)
                {
                    // Right-double-click detected
                    System.Diagnostics.Debug.WriteLine($"✅ Ctrl+Right-Double-Click detected at ({info.pt.x}, {info.pt.y})");
                    _lastRightClickTime = DateTime.MinValue; // Reset to avoid triple-click
                    
                    System.Diagnostics.Debug.WriteLine($"  → Firing DesktopDoubleRightClicked event");
                    WinApp.Current.Dispatcher.BeginInvoke(() => DesktopDoubleRightClicked?.Invoke());
                    
                    // Block the event so context menu doesn't appear
                    return new IntPtr(1);
                }
                else
                {
                    // Single right-click - track for double-click
                    System.Diagnostics.Debug.WriteLine($"🖱️ Single Ctrl+Right-click at ({info.pt.x}, {info.pt.y}) - tracking for double-click");
                    _lastRightClickTime = now;
                    _lastRightClickPt = info.pt;
                    
                    // Block single right-click to prevent context menu
                    return new IntPtr(1);
                }
            }
            else if (msg == WM_RBUTTONUP && IsDesktopArea(info.pt))
            {
                // Check if Ctrl was held
                bool ctrlPressed = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                
                if (ctrlPressed)
                {
                    // Block right-button-up as well to prevent context menu
                    System.Diagnostics.Debug.WriteLine($"🖱️ Right-Click UP blocked (Ctrl was held)");
                    return new IntPtr(1);
                }
            }
            else if (msg == WM_MOUSEMOVE && _dragging)
            {
                var s = ToDrawingPoint(_startPt);
                var c = ToDrawingPoint(info.pt);
                WinApp.Current.Dispatcher.BeginInvoke(() => _overlay?.UpdateDraw(s, c));
            }
            else if (msg == WM_LBUTTONUP && _dragging)
            {
                _dragging = false;
                var start = _startPt;
                var end   = info.pt;

                WinApp.Current.Dispatcher.BeginInvoke(() =>
                {
                    _overlay?.EndDraw();

                    double w = Math.Abs(end.x - start.x);
                    double h = Math.Abs(end.y - start.y);
                    if (w < MIN_DRAG_PX || h < MIN_DRAG_PX) return;

                    double scale = _overlay?.DpiScale ?? 1.0;
                    var rect = new Rect(
                        Math.Min(start.x, end.x) * scale,
                        Math.Min(start.y, end.y) * scale,
                        w * scale,
                        h * scale);

                    GridRequested?.Invoke(rect);
                });
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private bool IsDesktopArea(POINT pt)
    {
        // If the point is inside any grid window, don't intercept
        foreach (var r in _getGridBounds())
            if (r.Contains(pt.x, pt.y)) return false;

        // Check if the HWND at this point is the desktop shell
        var hwnd = WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return true;

        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, 256);
        var cls = sb.ToString();

        return cls is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32";
    }

    private static System.Drawing.Point ToDrawingPoint(POINT p) =>
        new(p.x, p.y);

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
