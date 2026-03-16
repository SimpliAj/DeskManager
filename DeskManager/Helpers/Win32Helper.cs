using System.Runtime.InteropServices;
using System.Text;
using System.IO;

namespace DeskManager.Helpers;

public static class Win32Helper
{
    public static readonly IntPtr HWND_BOTTOM = new(1);

    public const uint SWP_NOSIZE     = 0x0001;
    public const uint SWP_NOMOVE     = 0x0002;
    public const uint SWP_NOZORDER   = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    public const int GWL_EXSTYLE      = -20;
    public const int WS_EX_TOOLWINDOW  = 0x00000080;
    public const int WS_EX_NOACTIVATE  = 0x08000000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED     = 0x00080000;

    public const int WM_WINDOWPOSCHANGING = 0x0046;
    
    // ShowWindow constants
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public bool Contains(int x, int y) =>
            x >= Left && x <= Right && y >= Top && y <= Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public static void MakeToolWindow(IntPtr hwnd)
    {
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }

    /// Makes the window fully click-through (drawing-overlay use)
    public static void MakeClickThrough(IntPtr hwnd)
    {
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        style |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }

    public static void SendToBottom(IntPtr hwnd)
    {
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetFileAttributes(string lpFileName, uint dwFileAttributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern uint GetFileAttributes(string lpFileName);

    private const uint FILE_ATTRIBUTE_HIDDEN = 0x02;
    private const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

    /// Toggle file/folder hidden attribute on the desktop
    public static void SetFileHidden(string filePath, bool hidden)
    {
        try
        {
            // Never try to hide/unhide .lnk files (shortcuts)
            if (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"Skipping .lnk file: {filePath}");
                return;
            }

            if (!File.Exists(filePath) && !Directory.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"File doesn't exist: {filePath}");
                return; // File doesn't exist
            }

            uint attributes = GetFileAttributes(filePath);
            if (attributes == INVALID_FILE_ATTRIBUTES)
            {
                System.Diagnostics.Debug.WriteLine($"Can't access file attributes: {filePath}");
                return; // Can't access file
            }

            uint newAttributes = attributes;
            if (hidden)
            {
                newAttributes |= FILE_ATTRIBUTE_HIDDEN;   // Set hidden
                System.Diagnostics.Debug.WriteLine($"Attempting to HIDE: {filePath}");
            }
            else
            {
                newAttributes &= ~FILE_ATTRIBUTE_HIDDEN;  // Remove hidden
                System.Diagnostics.Debug.WriteLine($"Attempting to UNHIDE: {filePath}");
            }

            // Only call SetFileAttributes if attributes actually changed
            if (newAttributes != attributes)
            {
                bool success = SetFileAttributes(filePath, newAttributes);
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"✓ Success: {filePath}");
                }
                else
                {
                    int lastError = Marshal.GetLastWin32Error();
                    System.Diagnostics.Debug.WriteLine($"✗ FAILED for {filePath}: Win32 error {lastError}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"No attribute change needed: {filePath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in SetFileHidden for {filePath}: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static IntPtr FindDesktopListView()
    {
        var progmanHwnd = FindWindow("Progman", null);
        if (progmanHwnd == IntPtr.Zero)
            progmanHwnd = FindWindow("WorkerW", null);
        if (progmanHwnd == IntPtr.Zero)
            return IntPtr.Zero;

        var defViewHwnd = FindWindowEx(progmanHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defViewHwnd == IntPtr.Zero)
            return IntPtr.Zero;

        return FindWindowEx(defViewHwnd, IntPtr.Zero, "SysListView32", null);
    }

    /// Toggle desktop icons visibility
    public static void ToggleDesktopIcons(bool show)
    {
        var listViewHwnd = FindDesktopListView();
        if (listViewHwnd != IntPtr.Zero)
        {
            ShowWindow(listViewHwnd, show ? SW_SHOW : SW_HIDE);
        }
        else
        {
            // Fallback: directly hide/show Progman
            var progmanHwnd = FindWindow("Progman", null);
            if (progmanHwnd != IntPtr.Zero)
                ShowWindow(progmanHwnd, show ? SW_SHOW : SW_HIDE);
        }
    }

    /// Refresh desktop to show newly added/removed files
    public static void RefreshDesktop()
    {
        try
        {
            var listViewHwnd = FindDesktopListView();
            if (listViewHwnd != IntPtr.Zero)
            {
                SendMessage(listViewHwnd, 0x0015, (IntPtr)0, (IntPtr)0); // WM_PAINT
                PostMessage(listViewHwnd, 0x1014, (IntPtr)0, (IntPtr)0); // WM_GETTEXT
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error refreshing desktop: {ex.Message}");
        }
    }
}
