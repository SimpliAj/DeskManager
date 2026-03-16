using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DeskManager.Helpers;

namespace DeskManager.Windows;

public partial class DrawingOverlay : Window
{
    /// DPI scale factor: physical pixels → WPF DIPs
    public double DpiScale { get; private set; } = 1.0;

    public DrawingOverlay()
    {
        InitializeComponent();

        // Cover entire virtual screen (all monitors)
        Left   = SystemParameters.VirtualScreenLeft;
        Top    = SystemParameters.VirtualScreenTop;
        Width  = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Loaded += (_, _) =>
        {
            var helper = new WindowInteropHelper(this);
            Win32Helper.MakeClickThrough(helper.Handle);

            var src = PresentationSource.FromVisual(this);
            DpiScale = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        };
    }

    public void BeginDraw(System.Drawing.Point physPt)
    {
        UpdateRect(physPt, physPt);
        SelectRect.Visibility = Visibility.Visible;
        if (!IsVisible) Show();
    }

    public void UpdateDraw(System.Drawing.Point start, System.Drawing.Point cur)
    {
        UpdateRect(start, cur);
    }

    public void EndDraw()
    {
        SelectRect.Visibility = Visibility.Collapsed;
        Hide();
    }

    private void UpdateRect(System.Drawing.Point a, System.Drawing.Point b)
    {
        double x = Math.Min(a.X, b.X) * DpiScale;
        double y = Math.Min(a.Y, b.Y) * DpiScale;
        double w = Math.Abs(b.X - a.X) * DpiScale;
        double h = Math.Abs(b.Y - a.Y) * DpiScale;

        Canvas.SetLeft(SelectRect, x);
        Canvas.SetTop(SelectRect,  y);
        SelectRect.Width  = w;
        SelectRect.Height = h;
    }
}
