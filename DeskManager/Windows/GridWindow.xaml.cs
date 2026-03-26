using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
using Image           = System.Windows.Controls.Image;
using Brushes         = System.Windows.Media.Brushes;
using Cursors         = System.Windows.Input.Cursors;
using DataObject      = System.Windows.DataObject;
using DataFormats     = System.Windows.DataFormats;
using Color           = System.Windows.Media.Color;
using Button          = System.Windows.Controls.Button;

namespace DeskManager.Windows;

public partial class GridWindow : Window
{
    // Static variable to track which window is being dragged with Ctrl
    public static GridWindow? CurrentDragSource { get; set; }

    public GridData GridData { get; }

    public bool IsCollapsed { get; private set; }

    private GridData? _currentDisplayedGrid; // Track which grid's items are currently displayed (parent or child)

    private readonly GridManager _manager;
    private readonly FileStorageService _fileStorage = new();
    private bool _isDeleting; // Guard against multiple delete attempts
    private readonly FolderWatcherService _folderWatcher = new();
    private HwndSource? _hwndSource;
    private double _expandedHeight;
    private DispatcherTimer? _autoSaveTimer;
    private bool _hasPendingChanges;
    private DispatcherTimer? _doubleClickTimer;

    private enum SnapEdge { None, Top, Bottom, Left, Right }
    private SnapEdge _snapEdge = SnapEdge.None;
    private bool _snapping;  // guard against recursive LocationChanged
    private bool _isLayoutFlipped; // true when title bar is at bottom (top-snap expanded)
    private bool _pinnedExpanded;  // true when user explicitly expanded (button or ExpandAll) — suppresses peek-collapse on MouseLeave
    private DispatcherTimer? _peekTimer;   // polls cursor position for peek expand/collapse on snapped windows
    private bool _wasMouseOver;            // last known hover state from peek poll

    /// Win32 handle — used by GridManager for physical-pixel bounds.
    public IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    public GridWindow(GridData data, GridManager manager)
    {
        GridData = data;
        _manager = manager;
        // GridData stores "logical" height (window height minus 12px overhead for shadow/resize)
        _expandedHeight = data.Height + 12;

        InitializeComponent();

        // Restore position & size
        Left   = data.X;
        Top    = data.Y;
        Width  = data.Width + 8;        // +8 for drop shadow margin (4px each side)
        Height = data.Collapsed ? 38 : data.Height + 12; // 38 = 30 title + 8 margin

        // Validate window is within screen bounds
        ValidateWindowPosition();

        TitleLabel.Text = data.Title;

        // Single click on the title text → rename
        TitleLabel.MouseLeftButtonDown += (_, e) =>
        {
            // Only trigger if clicked directly on TitleLabel
            var hitTest = VisualTreeHelper.HitTest(TitleLabel, e.GetPosition(TitleLabel));
            if (hitTest == null)
            {
                e.Handled = true;
                return; // Click was outside TitleLabel bounds
            }

            // Single click - start timer for rename (after 300ms if not another click)
            if (_doubleClickTimer == null)
            {
                _doubleClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _doubleClickTimer.Tick += (_, _) =>
                {
                    _doubleClickTimer.Stop();
                    System.Diagnostics.Debug.WriteLine("📝 Single-click - edit title");
                    BeginEditTitle();
                };
            }

            _doubleClickTimer.Stop();
            _doubleClickTimer.Start();
            e.Handled = true;
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

        // Ensure empty hint is correctly displayed
        RefreshEmptyHint();

        // Magnetic snap: runs on every position change while dragging
        LocationChanged += (_, _) => 
        {
            ApplyMagneticSnap();
            ScheduleAutoSave();
        };

        // Auto-save on size changes
        SizeChanged += (_, _) => ScheduleAutoSave();

        // Peek handled via Win32 WM_MOUSEMOVE / WM_MOUSELEAVE in WndProc (reliable from app start)

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
        
        // Initialize snap edge after first render — ActualWidth/ActualHeight are correct at Render priority
        Dispatcher.BeginInvoke(() => InitializeSnapEdge(), System.Windows.Threading.DispatcherPriority.Render);
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

    // Called by GridManager before saving
    public void FlushPositionToData()
    {
        GridData.X         = Left;
        GridData.Y         = Top;
        GridData.Collapsed = IsCollapsed;

        if (!IsCollapsed)
        {
            GridData.Width  = ActualWidth  - 8;
            GridData.Height = ActualHeight - 12;
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

        // Top-edge + collapsed on load: fix corner radius (CollapseInstant ran before snap was known)
        if (_snapEdge == SnapEdge.Top && IsCollapsed)
            TitleBarBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);

        UpdatePeekTimer();
        System.Diagnostics.Debug.WriteLine($"🎯 Grid '{GridData.Title}' snap edge detected on load: {_snapEdge}");
    }

    /// Start or stop the peek poll timer based on whether the window is currently snapped.
    private void UpdatePeekTimer()
    {
        if (_snapEdge != SnapEdge.None)
        {
            if (_peekTimer == null)
            {
                _peekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _peekTimer.Tick += PeekPollTick;
            }
            _peekTimer.Start();
        }
        else
        {
            _peekTimer?.Stop();
        }
    }

    private void PeekPollTick(object? sender, EventArgs e)
    {
        if (_snapEdge == SnapEdge.None) { _peekTimer?.Stop(); return; }

        Win32Helper.GetCursorPos(out var pt);
        Win32Helper.GetWindowRect(Hwnd, out var rect);
        bool isOver = rect.Contains(pt.X, pt.Y);

        if (isOver == _wasMouseOver) return;
        _wasMouseOver = isOver;

        if (isOver && IsCollapsed)
            Expand();
        else if (!isOver && !IsCollapsed && !_pinnedExpanded)
            Collapse();
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

        UpdatePeekTimer();
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

    private void ValidateWindowPosition()
    {
        // Get available work area
        var (waLeft, waTop, waRight, waBottom) = GetWorkArea();
        
        // Check if window is completely outside screen bounds
        bool tooLeft = Left + Width < waLeft + 20;
        bool tooRight = Left > waRight - 20;
        bool tooTop = Top + 30 < waTop + 20;
        bool tooBottom = Top > waBottom - 20;
        
        if (tooLeft || tooRight || tooTop || tooBottom)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Window '{GridData.Title}' was out of bounds. Resetting to center.");
            // Reset to center of screen
            Left = waLeft + (waRight - waLeft) / 2 - Width / 2;
            Top = waTop + (waBottom - waTop) / 2 - Height / 2;
        }
    }

    // ─── Title Bar ──────────────────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Only edit title if TitleLabel is visible (not in tab mode)
            if (TitleLabel.Visibility == Visibility.Visible)
            {
                BeginEditTitle();
            }
            e.Handled = true;
            return;
        }

        // Check if Ctrl is being held for grid-to-grid grouping
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            CurrentDragSource = this;
            var data = new DataObject("GridWindow", this);
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
            CurrentDragSource = null;
            e.Handled = true;
            return;
        }

        // Normal DragMove for repositioning (without Ctrl)
        DragMove();
        Win32Helper.SendToBottom(new WindowInteropHelper(this).Handle);
        CheckEdgeSnap();
        FlushPositionToData();
        e.Handled = true;
    }

    private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // This is handled by the events above
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Cleanup
    }

    private void TitleBar_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();

        AddMenuItem(menu, "Rename",         () => BeginEditTitle());
        AddMenuItem(menu, "Link to Folder…", PickFolder);
        if (GridData.FolderPath != null)
            AddMenuItem(menu, "Unlink Folder", ClearFolder);

        // Add "Ungroup" option if this grid has child grids
        if (GridData.ChildGridIds.Count > 0)
        {
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "Ungroup", () =>
            {
                // Ungroup all child grids
                var childIds = GridData.ChildGridIds.ToList();
                foreach (var childId in childIds)
                {
                    var childGrid = _manager.FindGridById(childId);
                    if (childGrid != null)
                        _manager.UngroupGrid(childGrid);
                }
            });
        }

        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Delete Grid", ConfirmDelete);

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

    /// Called by GridManager after draw-create to immediately rename.
    public void BeginEditTitlePublic() => BeginEditTitle();

    private void BeginEditTitle()
    {
        TitleBox.Text = GridData.Title;
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
            GridData.Title = text;
            TitleLabel.Text = text;
            _manager.SaveConfig();
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

    private void ToggleCollapse()
    {
        if (IsCollapsed)
            Expand();
        else
            Collapse();
    }

    // ─── Collapse / Expand ──────────────────────────────────────────────────

    private int _collapseBtnClickCount;
    private DispatcherTimer? _collapseBtnTimer;

    private void CollapseBtn_Click(object sender, RoutedEventArgs e)
    {
        _collapseBtnClickCount++;
        
        if (_collapseBtnTimer == null)
        {
            _collapseBtnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _collapseBtnTimer.Tick += (_, _) =>
            {
                _collapseBtnTimer.Stop();
                
                if (_collapseBtnClickCount == 2)
                {
                    // Double-click → toggle all grids (any open → collapse all, all closed → expand all)
                    System.Diagnostics.Debug.WriteLine("🔘 Double-click on collapse button - toggling ALL grids");
                    _manager.ToggleAllCollapse();
                }
                else
                {
                    // Single click → toggle only this grid
                    System.Diagnostics.Debug.WriteLine("🔘 Single-click on collapse button - toggling this grid");
                    if (IsCollapsed) { _pinnedExpanded = true;  Expand();   }
                    else             { _pinnedExpanded = false; Collapse(); }
                }
                
                _collapseBtnClickCount = 0;
            };
        }
        
        _collapseBtnTimer.Stop();
        _collapseBtnTimer.Start();
    }

    // Flip title bar to bottom row (top-snap expanded) or restore to top (collapsed/normal)
    private void FlipLayoutForTopSnap(bool flip)
    {
        if (flip == _isLayoutFlipped) return;
        _isLayoutFlipped = flip;

        if (flip)
        {
            Grid.SetRow(TitleBarBorder, 2);
            TitleRow.Height             = new GridLength(0);
            ResizeRow.Height            = new GridLength(30);
            TitleBarBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);
        }
        else
        {
            Grid.SetRow(TitleBarBorder, 0);
            TitleRow.Height             = new GridLength(30);
            ResizeRow.Height            = new GridLength(8);
            TitleBarBorder.CornerRadius = new CornerRadius(8, 8, 0, 0);
        }
    }

    private void Collapse()
    {
        _expandedHeight = ActualHeight;
        CollapseInstant();
    }

    private void CollapseInstant()
    {
        IsCollapsed            = true;
        _pinnedExpanded        = false;
        ContentArea.Visibility = Visibility.Collapsed;
        ResizeStrip.Visibility = Visibility.Collapsed;

        // Restore normal layout before collapsing (title bar back to top)
        if (_isLayoutFlipped)
            FlipLayoutForTopSnap(false);

        ResizeRow.Height  = new GridLength(0);
        CollapseIcon.Text = "▸";
        Height            = 38;

        // Bottom-edge: slide the title strip to the very bottom of the screen
        if (_snapEdge == SnapEdge.Bottom)
        {
            var (_, _, _, waBottom) = GetWorkArea();
            Top = waBottom - 38;
        }

        // Top-edge: collapsed strip hangs down from screen edge → round bottom corners
        if (_snapEdge == SnapEdge.Top)
            TitleBarBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);

        GridData.Collapsed = true;
    }

    private void Expand()
    {
        IsCollapsed            = false;
        ContentArea.Visibility = Visibility.Visible;
        CollapseIcon.Text      = "▾";

        double h = _expandedHeight > 50 ? _expandedHeight : GridData.Height + 12;
        Height = h;

        if (_snapEdge == SnapEdge.Top)
        {
            // Flip: title bar moves to bottom, content fills above it
            FlipLayoutForTopSnap(true);
            ResizeStrip.Visibility = Visibility.Collapsed;
        }
        else
        {
            ResizeStrip.Visibility = Visibility.Visible;
            ResizeRow.Height       = new GridLength(8);

            if (_snapEdge == SnapEdge.Bottom)
            {
                var (_, _, _, waBottom) = GetWorkArea();
                Top = waBottom - h;
            }
        }

        GridData.Collapsed = false;
    }

    /// Public method to expand this grid (for ExpandAll functionality)
    public void PublicExpand()
    {
        _pinnedExpanded = true;
        Expand();
    }

    /// Public method to collapse this grid (for CollapseAll functionality)
    public void PublicCollapse()
    {
        Collapse();
    }
    // ─── Remove Grid ───────────────────────────────────────────────────────

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ConfirmDelete();
    }

    private void ConfirmDelete()
    {
        if (_isDeleting) return;
        _isDeleting = true;

        // Expand so the overlay is visible (grid might be collapsed)
        if (IsCollapsed) Expand();

        DeleteConfirmOverlay.Visibility = Visibility.Visible;
    }

    private void DeleteConfirmYes_Click(object sender, RoutedEventArgs e)
    {
        DeleteConfirmOverlay.Visibility = Visibility.Collapsed;
        _manager.DeleteGrid(this);
    }

    private void DeleteConfirmNo_Click(object sender, RoutedEventArgs e)
    {
        DeleteConfirmOverlay.Visibility = Visibility.Collapsed;
        _isDeleting = false;
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
        var itemsToRemove = new List<IconItemData>();
        
        foreach (var item in items)
        {
            // Validate that the file/folder still exists
            if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Item file not found on startup: {item.Path}");
                itemsToRemove.Add(item);
                continue;
            }
            
            AppendIconControl(item);
        }
        
        // Remove items that no longer exist
        if (itemsToRemove.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"🗑️ Removing {itemsToRemove.Count} missing items from grid");
            foreach (var item in itemsToRemove)
            {
                if (_currentDisplayedGrid != null)
                    _currentDisplayedGrid.Items.Remove(item);
                else
                    GridData.Items.Remove(item);
            }
            _manager.SaveConfig();
        }
        
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

        // ✕ click: remove from grid
        removeBtn.Click += (_, _) =>
        {
            System.Diagnostics.Debug.WriteLine($"\n🗑️ REMOVE BUTTON CLICKED");
            System.Diagnostics.Debug.WriteLine($"  Item Name: {item.Name}");
            System.Diagnostics.Debug.WriteLine($"  Item Path: {item.Path}");
            System.Diagnostics.Debug.WriteLine($"  Item OriginalPath: {item.OriginalPath}");
            
            // If file has an original path, move it back from storage
            if (!string.IsNullOrEmpty(item.OriginalPath))
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"  → Attempting restore to: {item.OriginalPath}");
                    // Check if file exists in storage
                    bool existsInStorage = File.Exists(item.Path) || Directory.Exists(item.Path);
                    System.Diagnostics.Debug.WriteLine($"  → File exists in storage: {existsInStorage}");

                    if (existsInStorage)
                    {
                        bool moveSuccess = _fileStorage.MoveFromStorage(item.Path, item.OriginalPath);
                        System.Diagnostics.Debug.WriteLine($"  → MoveFromStorage returned: {moveSuccess}");
                        
                        if (moveSuccess)
                        {
                            bool nowAtDesktop = File.Exists(item.OriginalPath) || Directory.Exists(item.OriginalPath);
                            System.Diagnostics.Debug.WriteLine($"  → File now at original path: {nowAtDesktop}");
                            if (nowAtDesktop)
                            {
                                System.Diagnostics.Debug.WriteLine($"✅ Successfully restored to desktop: {item.OriginalPath}");
                                // Refresh desktop to show newly added file
                                Helpers.Win32Helper.RefreshDesktop();
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"❌ MoveFromStorage claimed success but file not at destination!");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ MoveFromStorage failed/returned false");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ File not found in storage at: {item.Path}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Exception during restore: {ex.GetType().Name}: {ex.Message}");
                }
            }
            // If it's a .lnk Windows shortcut, just remove from grid
            else if (item.Path.StartsWith("::"))
            {
                System.Diagnostics.Debug.WriteLine($"Removing Shell object: {item.Path}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"  → No OriginalPath set (likely .lnk shortcut)");
            }

            GridData.Items.Remove(item);
            IconPanel.Children.Remove(container);
            RefreshEmptyHint();
            _manager.SaveConfig();
            System.Diagnostics.Debug.WriteLine($"✅ Item removed from grid\n");
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

        // Drag out of grid
        stack.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.Source == stack)
            {
                var data = new DataObject();
                data.SetData(DataFormats.FileDrop, new[] { item.Path });
                data.SetData("IsIconDrag", true); // Mark this as icon drag
                DragDrop.DoDragDrop(stack, data, DragDropEffects.Copy | DragDropEffects.Move);
            }
        };

        IconPanel.Children.Add(container);
    }

    private void ShowIconMenu(IconItemData item, FrameworkElement container)
    {
        var menu = new ContextMenu();
        AddMenuItem(menu, "Öffnen", () => LaunchItem(item));
        if (GridData.FolderPath == null)
        {
            AddMenuItem(menu, "Aus Grid entfernen", () =>
            {
                System.Diagnostics.Debug.WriteLine($"\n🗑️ CONTEXT MENU: REMOVE FROM GRID");
                System.Diagnostics.Debug.WriteLine($"  Item Name: {item.Name}");
                System.Diagnostics.Debug.WriteLine($"  Item Path: {item.Path}");
                System.Diagnostics.Debug.WriteLine($"  Item OriginalPath: {item.OriginalPath}");
                
                // If file has an original path, move it back from storage
                if (!string.IsNullOrEmpty(item.OriginalPath))
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"  → Attempting restore to: {item.OriginalPath}");
                        // Check if file exists in storage
                        bool existsInStorage = File.Exists(item.Path) || Directory.Exists(item.Path);
                        System.Diagnostics.Debug.WriteLine($"  → File exists in storage: {existsInStorage}");

                        if (existsInStorage)
                        {
                            bool moveSuccess = _fileStorage.MoveFromStorage(item.Path, item.OriginalPath);
                            System.Diagnostics.Debug.WriteLine($"  → MoveFromStorage returned: {moveSuccess}");
                            
                            if (moveSuccess)
                            {
                                bool nowAtDesktop = File.Exists(item.OriginalPath) || Directory.Exists(item.OriginalPath);
                                System.Diagnostics.Debug.WriteLine($"  → File now at original path: {nowAtDesktop}");
                                if (nowAtDesktop)
                                {
                                    System.Diagnostics.Debug.WriteLine($"✅ Successfully restored to: {item.OriginalPath}");
                                    // Refresh desktop to show newly restored file
                                    Helpers.Win32Helper.RefreshDesktop();
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"❌ MoveFromStorage claimed success but file not at destination!");
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"❌ MoveFromStorage failed/returned false");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ File not found in storage at: {item.Path}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Exception during restore: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"  → No OriginalPath set");
                }
                
                GridData.Items.Remove(item);
                IconPanel.Children.Remove(container);
                RefreshEmptyHint();
                _manager.SaveConfig();
                System.Diagnostics.Debug.WriteLine($"✅ Item removed from grid\n");
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

    // Window-level handlers: catch drops anywhere on the grid, not just the WrapPanel.
    // This is the fix for AllowsTransparency + external app drops (Explorer etc.)
    private void Window_Drop(object sender, DragEventArgs e)
    {
        // Ignore icon drag operations (they go to IconPanel_Drop)
        if (e.Data.GetDataPresent("IsIconDrag"))
        {
            e.Handled = false;
            return;
        }

        // Check if another GridWindow is being dropped via Ctrl+Drag
        if (CurrentDragSource != null && CurrentDragSource.GridData.Id != GridData.Id)
        {
            _manager.GroupGrids(GridData, CurrentDragSource.GridData);
            CurrentDragSource = null;
            e.Handled = true;
            e.Effects = DragDropEffects.Copy;
            return;
        }

        // Check if another GridWindow is being dropped via DataObject
        if (e.Data.GetDataPresent("GridWindow"))
        {
            if (e.Data.GetData("GridWindow") is GridWindow droppedWindow &&
                droppedWindow.GridData.Id != GridData.Id)
            {
                _manager.GroupGrids(GridData, droppedWindow.GridData);
            }
            e.Handled = true;
            return;
        }

        HandleFileDrop(e);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        // Ignore icon drag operations
        if (e.Data.GetDataPresent("IsIconDrag"))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = false;
            return;
        }

        if (e.Data.GetDataPresent("GridWindow"))
        {
            // Another grid is being dragged over this one
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

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
        System.Diagnostics.Debug.WriteLine($"HandleFileDrop called, DataFormats: {string.Join(", ", e.Data.GetFormats())}");
        
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) 
        {
            System.Diagnostics.Debug.WriteLine("No FileDrop format found");
            return;
        }
        
        // Use the currently displayed grid (might be a tab child grid)
        GridData targetGrid = _currentDisplayedGrid ?? GridData;
        
        if (targetGrid.FolderPath != null) return; // folder portals are read-only

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        System.Diagnostics.Debug.WriteLine($"Files dropped: {string.Join(", ", files)}");
        
        foreach (var file in files)
        {
            System.Diagnostics.Debug.WriteLine($"Processing file: {file}");
            
            // Skip if already in grid
            if (targetGrid.Items.Any(i => i.Path == file || i.OriginalPath == file))
            {
                System.Diagnostics.Debug.WriteLine($"Already in grid: {file}");
                continue;
            }

            // Skip special paths (Recycle Bin, etc.)
            if (file.StartsWith("::"))
            {
                System.Diagnostics.Debug.WriteLine($"Skipping Shell object: {file}");
                continue;
            }

            // Verify file exists before adding
            if (!File.Exists(file) && !Directory.Exists(file))
            {
                System.Diagnostics.Debug.WriteLine($"File doesn't exist: {file}");
                continue;
            }

            // Move ALL files to storage (including .lnk shortcuts)
            // This ensures they disappear from desktop
            System.Diagnostics.Debug.WriteLine($"Moving to storage: {file}");
            string pathToUse = _fileStorage.MoveToStorage(file);
            System.Diagnostics.Debug.WriteLine($"Now at: {pathToUse}");

            // VALIDATION: Make sure the file was actually moved to storage
            // If pathToUse == file, the move failed - skip adding this item
            if (pathToUse == file)
            {
                System.Diagnostics.Debug.WriteLine($"❌ FAILED TO MOVE TO STORAGE - SKIPPING: {file}");
                System.Windows.MessageBox.Show(
                    $"⚠️ Fehler beim Verschieben:\n{Path.GetFileName(file)}\n\nGrund:\n- Datei ist zu groß\n- OneDrive blockiert den Zugriff\n- Berechtigungsfehler\n\nDie Datei wird NICHT hinzugefügt.",
                    "Verschiebungsfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                continue;
            }

            // Final verification: ensure the file actually exists at the new location
            if (!File.Exists(pathToUse) && !Directory.Exists(pathToUse))
            {
                System.Diagnostics.Debug.WriteLine($"❌ CRITICAL: File moved but then disappeared: {pathToUse}");
                System.Windows.MessageBox.Show(
                    $"❌ KRITISCHER FEHLER:\n{Path.GetFileName(file)}\n\nDie Datei wurde gelöscht, konnte aber nicht gespeichert werden!\n\nBitte versuchen Sie es später erneut.",
                    "Speicherfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                continue;
            }

            var item = new IconItemData
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Path = pathToUse,
                OriginalPath = file  // Always store original path for restoration
            };

            targetGrid.Items.Add(item);
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
        GridData.FolderPath = path;
        TitleLabel.Text = GridData.Title + " \uD83D\uDCC2";
        _folderWatcher.FolderChanged += RefreshPortal;
        _folderWatcher.Watch(path);
        RefreshPortal();
    }

    private void RefreshPortal()
    {
        Dispatcher.Invoke(() =>
        {
            if (GridData.FolderPath == null) return;

            IconPanel.Children.Clear();
            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(GridData.FolderPath).Take(60))
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
            Description        = "Select folder to link to this grid",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            SetupFolderPortal(dlg.SelectedPath);
    }

    private void ClearFolder()
    {
        _folderWatcher.Stop();
        GridData.FolderPath = null;
        GridData.Items.Clear();
        TitleLabel.Text = GridData.Title;
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

    // ─── Tab Management (Grouped Grids) ─────────────────────────────────────

    /// Refreshes the tab bar based on child grids
    public void RefreshTabs()
    {
        TabBar.Children.Clear();

        // Always show tabs if there are child grids
        if (GridData.ChildGridIds.Count == 0)
        {
            TabBar.Visibility = Visibility.Collapsed;
            TitleLabel.Visibility = Visibility.Visible;
            return;
        }

        // Add button for parent grid title first
        var parentBtn = new Button
        {
            Content = GridData.Title,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(2, 0, 2, 0),
            Background = _currentDisplayedGrid == null ? new SolidColorBrush(Color.FromArgb(180, 80, 80, 120)) : new SolidColorBrush(Color.FromArgb(80, 60, 60, 80)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        parentBtn.Click += (_, _) => ShowParentContent();
        TabBar.Children.Add(parentBtn);

        // Add separator
        TabBar.Children.Add(new Separator 
        { 
            Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            Margin = new Thickness(4, 5, 4, 5),
            Width = 1,
            VerticalAlignment = VerticalAlignment.Stretch
        });

        // Add buttons for child grids
        foreach (var childId in GridData.ChildGridIds)
        {
            var childGrid = _manager.FindGridById(childId);
            if (childGrid == null) continue;

            var isActive = _currentDisplayedGrid?.Id == childGrid.Id;
            var button = new Button
            {
                Content = childGrid.Title,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(2, 0, 2, 0),
                Background = isActive ? new SolidColorBrush(Color.FromArgb(180, 80, 80, 120)) : new SolidColorBrush(Color.FromArgb(80, 60, 60, 80)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 11,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            button.Click += (_, _) => SwitchToTab(childGrid);
            TabBar.Children.Add(button);
        }

        TabBar.Visibility = Visibility.Visible;
        TitleLabel.Visibility = Visibility.Collapsed;
    }

    private void ShowParentContent()
    {
        _currentDisplayedGrid = null; // Displaying parent
        ContentArea.Visibility = Visibility.Visible;
        LoadIcons(GridData.Items);
        RefreshTabs(); // Update tab colors to show which is active
    }

    /// Switches to display a child grid's items
    public void SwitchToTab(GridData childGrid)
    {
        _currentDisplayedGrid = childGrid;
        ContentArea.Visibility = Visibility.Visible;
        LoadIcons(childGrid.Items);
        RefreshTabs(); // Update tab colors to show which is active
    }

    protected override void OnClosed(EventArgs e)
    {
        _folderWatcher.Dispose();
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
        _autoSaveTimer?.Stop();
        _doubleClickTimer?.Stop();
        _collapseBtnTimer?.Stop();
        _peekTimer?.Stop();
        base.OnClosed(e);
    }
}
