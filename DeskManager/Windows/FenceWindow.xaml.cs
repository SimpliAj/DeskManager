using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskManager.Helpers;
using DeskManager.Models;
using DeskManager.Services;
using WinForms = System.Windows.Forms;
// Disambiguate WPF vs WinForms/Drawing types that share the same short name
using DragEventArgs   = System.Windows.DragEventArgs;
using KeyEventArgs    = System.Windows.Input.KeyEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using MessageBox      = System.Windows.MessageBox;
using Image           = System.Windows.Controls.Image;
using Brushes         = System.Windows.Media.Brushes;
using Cursors         = System.Windows.Input.Cursors;
using DataObject      = System.Windows.DataObject;
using DataFormats     = System.Windows.DataFormats;
using Color           = System.Windows.Media.Color;
using Button          = System.Windows.Controls.Button;

namespace DeskManager.Windows;

public partial class FenceWindow : Window
{
    public FenceData FenceData { get; }

    public bool IsCollapsed { get; private set; }

    private readonly FenceManager _manager;
    private readonly FolderWatcherService _folderWatcher = new();
    private HwndSource? _hwndSource;
    private double _expandedHeight;
    private DispatcherTimer? _autoSaveTimer;
    private bool _hasPendingChanges;

    private enum SnapEdge { None, Top, Bottom, Left, Right }
    private SnapEdge _snapEdge = SnapEdge.None;
    private bool _snapping;  // guard against recursive LocationChanged

    /// Win32 handle — used by FenceManager for physical-pixel bounds.
    public IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    public FenceWindow(FenceData data, FenceManager manager)
    {
        FenceData = data;
        _manager = manager;
        // FenceData stores "logical" height (window height minus 12px overhead for shadow/resize)
        _expandedHeight = data.Height + 12;

        InitializeComponent();

        // Restore position & size
        Left   = data.X;
        Top    = data.Y;
        Width  = data.Width + 8;        // +8 for drop shadow margin (4px each side)
        Height = data.Collapsed ? 38 : data.Height + 12; // 38 = 30 title + 8 margin

        TitleLabel.Text = data.Title;

        // Single click on the title text → rename (doesn't bubble to DragMove)
        TitleLabel.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            BeginEditTitle();
        };

        if (data.Collapsed)
            CollapseInstant();

        if (data.FolderPath != null)
            SetupFolderPortal(data.FolderPath);
        else
            LoadIcons(data.Items);

        Loaded += OnLoaded;
    }

    // ─── Win32 Desktop Integration ─────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);

        Win32Helper.MakeToolWindow(helper.Handle);
        Win32Helper.SendToBottom(helper.Handle);

        // Magnetic snap: runs on every position change while dragging
        LocationChanged += (_, _) => 
        {
            ApplyMagneticSnap();
            ScheduleAutoSave();
        };

        // Auto-save on size changes
        SizeChanged += (_, _) => ScheduleAutoSave();

        // Peek: expand on hover when edge-snapped, re-collapse on leave
        MouseEnter += (_, _) => { if (_snapEdge != SnapEdge.None && IsCollapsed) Expand(); };
        MouseLeave += (_, _) => { if (_snapEdge != SnapEdge.None && !IsCollapsed) Collapse(); };

        // Setup auto-save timer (debounced, 1 second delay)
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoSaveTimer.Tick += (_, _) =>
        {
            if (_hasPendingChanges)
            {
                _autoSaveTimer.Stop();
                FlushPositionToData();
                _manager.SaveConfig();
                _hasPendingChanges = false;
            }
        };
        
        // Initialize snap edge on first render - ensures auto-collapse works after restart
        Loaded += (_, _) => 
        {
            // Delay slightly to ensure ActualWidth/ActualHeight are correct
            Dispatcher.BeginInvoke(() => InitializeSnapEdge(), System.Windows.Threading.DispatcherPriority.Render);
        };
    }

    private void ScheduleAutoSave()
    {
        _hasPendingChanges = true;
        if (_autoSaveTimer?.IsEnabled != true)
            _autoSaveTimer?.Start();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Helper.WM_WINDOWPOSCHANGING)
        {
            var wp = Marshal.PtrToStructure<Win32Helper.WINDOWPOS>(lParam);

            // Always keep at HWND_BOTTOM (desktop level) unless already there
            if (wp.hwndInsertAfter != Win32Helper.HWND_BOTTOM)
            {
                wp.flags &= ~Win32Helper.SWP_NOZORDER;
                wp.hwndInsertAfter = Win32Helper.HWND_BOTTOM;
                Marshal.StructureToPtr(wp, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    // Called by FenceManager before saving
    public void FlushPositionToData()
    {
        FenceData.X         = Left;
        FenceData.Y         = Top;
        FenceData.Collapsed = IsCollapsed;

        if (!IsCollapsed)
        {
            FenceData.Width  = ActualWidth  - 8;
            FenceData.Height = ActualHeight - 12;
        }
    }

    /// Initialize snap edge on window load (fixes auto-collapse after restart)
    private void InitializeSnapEdge()
    {
        // Simulate LocationChanged to detect and set snap edge
        var wa = SystemParameters.WorkArea;
        const double threshold = 15;  // pixels

        bool nearTop = Top <= wa.Top + threshold;
        bool nearBottom = Top + ActualHeight >= wa.Bottom - threshold;
        bool nearLeft = Left <= wa.Left + threshold;
        bool nearRight = Left + ActualWidth >= wa.Right - threshold;

        if (nearBottom) _snapEdge = SnapEdge.Bottom;
        else if (nearTop) _snapEdge = SnapEdge.Top;
        else if (nearLeft) _snapEdge = SnapEdge.Left;
        else if (nearRight) _snapEdge = SnapEdge.Right;
        else _snapEdge = SnapEdge.None;

        // Activate window for mouse events to work properly
        if (_snapEdge != SnapEdge.None)
        {
            Focus();
        }

        // If snapped and collapsed, auto-collapse behavior should work on hover now
        System.Diagnostics.Debug.WriteLine($"🎯 Fence '{FenceData.Title}' snap edge detected on load: {_snapEdge}");
    }

    // ─── Edge Snapping ──────────────────────────────────────────────────────

    /// Runs after DragMove() — decides whether to snap+collapse.
    private void CheckEdgeSnap()
    {
        var (waLeft, waTop, waRight, waBottom) = GetWorkArea();
        const double px = 6;

        bool nearLeft   = Left               <= waLeft   + px;
        bool nearRight  = Left + ActualWidth >= waRight  - px;
        bool nearTop    = Top                <= waTop    + px;
        bool nearBottom = Top + ActualHeight >= waBottom - px;

        // Priority: bottom > top > sides
        if      (nearBottom) _snapEdge = SnapEdge.Bottom;
        else if (nearTop)    _snapEdge = SnapEdge.Top;
        else if (nearLeft)   _snapEdge = SnapEdge.Left;
        else if (nearRight)  _snapEdge = SnapEdge.Right;
        else                 _snapEdge = SnapEdge.None;

        if (_snapEdge != SnapEdge.None && !IsCollapsed)
            Collapse();
    }

    /// Runs on every LocationChanged — magnetic pull toward screen edges.
    private void ApplyMagneticSnap()
    {
        if (_snapping || IsCollapsed) return;
        _snapping = true;
        try
        {
            var (waLeft, waTop, waRight, waBottom) = GetWorkArea();
            const double magnet = 8;

            if (Math.Abs(Left - waLeft)                  < magnet) Left = waLeft;
            if (Math.Abs(Left + ActualWidth  - waRight)  < magnet) Left = waRight  - ActualWidth;
            if (Math.Abs(Top  - waTop)                   < magnet) Top  = waTop;
            if (Math.Abs(Top  + ActualHeight - waBottom) < magnet) Top  = waBottom - ActualHeight;
        }
        finally { _snapping = false; }
    }

    private (double waLeft, double waTop, double waRight, double waBottom) GetWorkArea()
    {
        var hwnd   = new WindowInteropHelper(this).Handle;
        var screen = WinForms.Screen.FromHandle(hwnd);
        var src    = PresentationSource.FromVisual(this);
        double dX  = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double dY  = src?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;
        var wa     = screen.WorkingArea;
        return (wa.Left * dX, wa.Top * dY, wa.Right * dX, wa.Bottom * dY);
    }

    // ─── Title Bar ──────────────────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BeginEditTitle();
            return;
        }

        // DragMove blocks until mouse released, then we re-pin to bottom
        DragMove();
        Win32Helper.SendToBottom(new WindowInteropHelper(this).Handle);
        CheckEdgeSnap();
        FlushPositionToData();
    }

    private void TitleBar_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();

        AddMenuItem(menu, "Rename",         () => BeginEditTitle());
        AddMenuItem(menu, "Link to Folder…", PickFolder);
        if (FenceData.FolderPath != null)
            AddMenuItem(menu, "Unlink Folder", ClearFolder);

        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Delete Fence", ConfirmDelete);

        menu.PlacementTarget = this;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static void AddMenuItem(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    // ─── Title Editing ──────────────────────────────────────────────────────

    /// Called by FenceManager after draw-create to immediately rename.
    public void BeginEditTitlePublic() => BeginEditTitle();

    private void BeginEditTitle()
    {
        TitleBox.Text = FenceData.Title;
        TitleLabel.Visibility = Visibility.Collapsed;
        TitleBox.Visibility   = Visibility.Visible;
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void CommitTitle()
    {
        var text = TitleBox.Text.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            FenceData.Title = text;
            TitleLabel.Text = text;
        }
        TitleBox.Visibility   = Visibility.Collapsed;
        TitleLabel.Visibility = Visibility.Visible;
    }

    private void TitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  { CommitTitle(); e.Handled = true; }
        if (e.Key == Key.Escape) { TitleBox.Visibility = Visibility.Collapsed; TitleLabel.Visibility = Visibility.Visible; }
    }

    private void TitleBox_LostFocus(object sender, RoutedEventArgs e) => CommitTitle();

    // ─── Collapse / Expand ──────────────────────────────────────────────────

    private void CollapseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsCollapsed) Expand();
        else             Collapse();
    }

    private void Collapse()
    {
        _expandedHeight = ActualHeight;
        CollapseInstant();
    }

    private void CollapseInstant()
    {
        IsCollapsed             = true;
        ContentArea.Visibility  = Visibility.Collapsed;
        ResizeStrip.Visibility  = Visibility.Collapsed;
        ResizeRow.Height        = new GridLength(0);
        CollapseIcon.Text       = "▸";
        Height                  = 38;

        // Bottom-edge: slide the title strip to the very bottom of the screen
        if (_snapEdge == SnapEdge.Bottom)
        {
            var (_, _, _, waBottom) = GetWorkArea();
            Top = waBottom - 38;
        }

        FenceData.Collapsed = true;
    }

    private void Expand()
    {
        IsCollapsed            = false;
        ContentArea.Visibility = Visibility.Visible;
        ResizeStrip.Visibility = Visibility.Visible;
        ResizeRow.Height       = new GridLength(8);
        CollapseIcon.Text      = "▾";

        double h = _expandedHeight > 50 ? _expandedHeight : FenceData.Height + 12;
        Height = h;

        // Bottom-edge: expand upward from the bottom
        if (_snapEdge == SnapEdge.Bottom)
        {
            var (_, _, _, waBottom) = GetWorkArea();
            Top = waBottom - h;
        }

        FenceData.Collapsed = false;
    }

    // ─── Remove Fence ───────────────────────────────────────────────────────

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => ConfirmDelete();

    private void ConfirmDelete()
    {
        var result = MessageBox.Show(
            $"Remove fence \"{FenceData.Title}\"?",
            "DeskManager",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            _manager.DeleteFence(this);
    }

    public void ForceClose()
    {
        try { _folderWatcher.Dispose(); } catch { }
        try { _hwndSource?.RemoveHook(WndProc); } catch { }
        Dispatcher.BeginInvoke(new Action(Close));
    }

    // ─── Resize ─────────────────────────────────────────────────────────────

    private void RightThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange);
    }

    private void BottomThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!IsCollapsed)
            Height = Math.Max(80, ActualHeight + e.VerticalChange);
    }

    private void CornerThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!IsCollapsed)
        {
            Width  = Math.Max(MinWidth, ActualWidth  + e.HorizontalChange);
            Height = Math.Max(80,       ActualHeight + e.VerticalChange);
            ScheduleAutoSave();
        }
    }

    // ─── Icon Management ────────────────────────────────────────────────────

    private void LoadIcons(List<IconItemData> items)
    {
        IconPanel.Children.Clear();
        foreach (var item in items)
            AppendIconControl(item);
        RefreshEmptyHint();
    }

    private void AppendIconControl(IconItemData item, bool allowRemove = true)
    {
        var icon = IconHelper.GetFileIcon(item.Path, large: true);

        var img = new Image
        {
            Source  = icon ?? MissingIcon(),
            Width   = 32,
            Height  = 32,
            Stretch = Stretch.Uniform
        };

        var lbl = new TextBlock
        {
            Text                = item.Name,
            Foreground          = Brushes.White,
            FontSize            = 10,
            TextWrapping        = TextWrapping.Wrap,
            TextAlignment       = TextAlignment.Center,
            MaxWidth            = 68,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        var stack = new StackPanel
        {
            Cursor  = Cursors.Hand,
            ToolTip = item.Path,
        };
        stack.Children.Add(img);
        stack.Children.Add(lbl);

        // ✕ remove button — visible on hover
        var removeBtn = new Button
        {
            Content             = "✕",
            Width               = 16,
            Height              = 16,
            FontSize            = 9,
            Padding             = new Thickness(0),
            Cursor              = Cursors.Hand,
            Visibility          = Visibility.Collapsed,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Top,
            Background          = new SolidColorBrush(Color.FromArgb(180, 200, 50, 50)),
            Foreground          = Brushes.White,
            BorderThickness     = new Thickness(0),
            ToolTip             = "Entfernen",
        };

        // Wrap in a Grid so the ✕ can float top-right over the stack
        var container = new Grid
        {
            Width  = 76,
            Margin = new Thickness(4),
        };
        container.Children.Add(stack);
        if (allowRemove) container.Children.Add(removeBtn);

        // Hover: show/hide ✕
        container.MouseEnter += (_, _) => removeBtn.Visibility = Visibility.Visible;
        container.MouseLeave += (_, _) => removeBtn.Visibility = Visibility.Collapsed;

        // ✕ click: remove from fence
        removeBtn.Click += (_, _) =>
        {
            FenceData.Items.Remove(item);
            IconPanel.Children.Remove(container);
            RefreshEmptyHint();
            _manager.SaveConfig();
        };

        // Double-click to launch
        stack.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) LaunchItem(item);
        };

        // Right-click context menu
        stack.MouseRightButtonDown += (_, e) =>
        {
            e.Handled = true;
            ShowIconMenu(item, container);
        };

        // Drag out of fence
        stack.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragDrop.DoDragDrop(stack,
                    new DataObject(DataFormats.FileDrop, new[] { item.Path }),
                    DragDropEffects.Copy | DragDropEffects.Move);
        };

        IconPanel.Children.Add(container);
    }

    private void ShowIconMenu(IconItemData item, FrameworkElement container)
    {
        var menu = new ContextMenu();
        AddMenuItem(menu, "Öffnen", () => LaunchItem(item));
        if (FenceData.FolderPath == null)
        {
            AddMenuItem(menu, "Aus Fence entfernen", () =>
            {
                FenceData.Items.Remove(item);
                IconPanel.Children.Remove(container);
                RefreshEmptyHint();
                _manager.SaveConfig();
            });
        }
        menu.PlacementTarget = container;
        menu.IsOpen = true;
    }

    private static void LaunchItem(IconItemData item)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = item.Path,
                UseShellExecute = true
            });
        }
        catch { /* ignore */ }
    }

    // ─── Drag & Drop ────────────────────────────────────────────────────────

    // Window-level handlers: catch drops anywhere on the fence, not just the WrapPanel.
    // This is the fix for AllowsTransparency + external app drops (Explorer etc.)
    private void Window_Drop(object sender, DragEventArgs e) => HandleFileDrop(e);
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void IconPanel_Drop(object sender, DragEventArgs e) => HandleFileDrop(e);

    private void IconPanel_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void HandleFileDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (FenceData.FolderPath != null) return; // folder portals are read-only

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (var file in files)
        {
            if (FenceData.Items.Any(i => i.Path == file)) continue;

            var item = new IconItemData
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Path = file
            };

            FenceData.Items.Add(item);
            AppendIconControl(item);
        }

        RefreshEmptyHint();
        _manager.SaveConfig();
        e.Handled = true;
    }

    private void RefreshEmptyHint()
    {
        EmptyHint.Visibility = IconPanel.Children.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ─── Folder Portal ──────────────────────────────────────────────────────

    private void SetupFolderPortal(string path)
    {
        FenceData.FolderPath = path;
        TitleLabel.Text = FenceData.Title + " \uD83D\uDCC2";
        _folderWatcher.FolderChanged += RefreshPortal;
        _folderWatcher.Watch(path);
        RefreshPortal();
    }

    private void RefreshPortal()
    {
        Dispatcher.Invoke(() =>
        {
            if (FenceData.FolderPath == null) return;

            IconPanel.Children.Clear();
            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(FenceData.FolderPath).Take(60))
                {
                    AppendIconControl(new IconItemData
                    {
                        Name = Path.GetFileName(entry),
                        Path = entry
                    }, allowRemove: false);
                }
            }
            catch { /* folder gone */ }

            RefreshEmptyHint();
        });
    }

    private void PickFolder()
    {
        var dlg = new WinForms.FolderBrowserDialog
        {
            Description        = "Select folder to link to this fence",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            SetupFolderPortal(dlg.SelectedPath);
    }

    private void ClearFolder()
    {
        _folderWatcher.Stop();
        FenceData.FolderPath = null;
        FenceData.Items.Clear();
        TitleLabel.Text = FenceData.Title;
        IconPanel.Children.Clear();
        RefreshEmptyHint();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// Fallback icon for files that have no shell icon.
    private static ImageSource MissingIcon()
    {
        var bmp = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgra32, null);
        return bmp;
    }

    protected override void OnClosed(EventArgs e)
    {
        _folderWatcher.Dispose();
        _hwndSource?.RemoveHook(WndProc);
        base.OnClosed(e);
    }
}
