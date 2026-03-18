using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskManager.Helpers;
using DeskManager.Models;
using DeskManager.Services;
using WinForms       = System.Windows.Forms;
using Button         = System.Windows.Controls.Button;
using MessageBox     = System.Windows.MessageBox;
using Color          = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace DeskManager.Windows;

enum SettingsPage { Welcome, Personalization, Hide, Spaces, General, Wiki, About }

public partial class SettingsWindow : Window
{
    private readonly GridManager _manager;

    private string _bgColor;
    private string _titleColor;
    private string _borderColor;
    private string _textColor;

    public SettingsWindow(GridManager manager)
    {
        _manager = manager;
        InitializeComponent();

        var theme = manager.GetTheme();
        _bgColor     = theme.GridBackground;
        _titleColor  = theme.TitleBarColor;
        _borderColor = theme.BorderColor;
        _textColor   = theme.TextColor;

        LoadThemeControls();
        LoadSpacesList();

        _manager.SpacesChanged += OnSpacesChanged;

        NavigateTo(SettingsPage.Welcome);
    }

    // ─── Navigation ─────────────────────────────────────────────────────────

    private void NavigateTo(SettingsPage page)
    {
        if (PageWelcome == null) return; // Called during InitializeComponent before panels exist

        PageWelcome.Visibility         = Visibility.Collapsed;
        PagePersonalization.Visibility = Visibility.Collapsed;
        PageHide.Visibility            = Visibility.Collapsed;
        PageSpaces.Visibility          = Visibility.Collapsed;
        PageGeneral.Visibility         = Visibility.Collapsed;
        PageWiki.Visibility            = Visibility.Collapsed;
        PageAbout.Visibility           = Visibility.Collapsed;

        switch (page)
        {
            case SettingsPage.Welcome:         PageWelcome.Visibility = Visibility.Visible; break;
            case SettingsPage.Personalization: PagePersonalization.Visibility = Visibility.Visible; break;
            case SettingsPage.Hide:            PageHide.Visibility = Visibility.Visible; break;
            case SettingsPage.Spaces:          PageSpaces.Visibility = Visibility.Visible; break;
            case SettingsPage.General:         PageGeneral.Visibility = Visibility.Visible; break;
            case SettingsPage.Wiki:            PageWiki.Visibility = Visibility.Visible; break;
            case SettingsPage.About:           PageAbout.Visibility = Visibility.Visible; break;
        }

        // Sync sidebar (index 5 is the non-selectable separator, so Wiki=6, About=7)
        int[] indexMap = { 0, 1, 2, 3, 4, 6, 7 };
        SidebarNav.SelectionChanged -= SidebarNav_SelectionChanged;
        SidebarNav.SelectedIndex = indexMap[(int)page];
        SidebarNav.SelectionChanged += SidebarNav_SelectionChanged;
    }

    private void SidebarNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox lb || lb.SelectedIndex < 0) return;
        SettingsPage page = lb.SelectedIndex switch
        {
            0 => SettingsPage.Welcome,
            1 => SettingsPage.Personalization,
            2 => SettingsPage.Hide,
            3 => SettingsPage.Spaces,
            4 => SettingsPage.General,
            6 => SettingsPage.Wiki,
            7 => SettingsPage.About,
            _ => SettingsPage.Welcome
        };
        NavigateTo(page);
    }

    // ─── Theme Controls ─────────────────────────────────────────────────────

    private void LoadThemeControls()
    {
        if (AutostartCheckbox != null)
            AutostartCheckbox.IsChecked = AutostartHelper.IsAutostartEnabled();

        if (NotificationsCheckbox != null)
            NotificationsCheckbox.IsChecked = _manager.Config.NotificationsEnabled;

        SetColorButton(BgColorBtn,     _bgColor);
        SetColorButton(TitleColorBtn,  _titleColor);
        SetColorButton(BorderColorBtn, _borderColor);
        SetColorButton(TextColorBtn,   _textColor);

        BgOpacity.ValueChanged     -= BgOpacity_Changed;
        TitleOpacity.ValueChanged  -= TitleOpacity_Changed;
        BorderOpacity.ValueChanged -= BorderOpacity_Changed;
        TextOpacity.ValueChanged   -= TextOpacity_Changed;

        BgOpacity.Value     = ThemeService.GetAlpha(_bgColor);
        TitleOpacity.Value  = ThemeService.GetAlpha(_titleColor);
        BorderOpacity.Value = ThemeService.GetAlpha(_borderColor);
        TextOpacity.Value   = ThemeService.GetAlpha(_textColor);

        BgOpacity.ValueChanged     += BgOpacity_Changed;
        TitleOpacity.ValueChanged  += TitleOpacity_Changed;
        BorderOpacity.ValueChanged += BorderOpacity_Changed;
        TextOpacity.ValueChanged   += TextOpacity_Changed;

        UpdateOpacityLabels();
        UpdatePreview();

        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string versionText = v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "—";
        VersionLabel.Text = versionText;
    }

    private static void SetColorButton(Button btn, string hex)
    {
        var c = ThemeService.ToDrawingColor(hex);
        btn.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
    }

    private void UpdateOpacityLabels()
    {
        BgOpacityLabel.Text     = $"{(int)BgOpacity.Value}";
        TitleOpacityLabel.Text  = $"{(int)TitleOpacity.Value}";
        BorderOpacityLabel.Text = $"{(int)BorderOpacity.Value}";
        TextOpacityLabel.Text   = $"{(int)TextOpacity.Value}";
    }

    private void UpdatePreview()
    {
        PreviewBorder.Background    = ParseBrush(_bgColor);
        PreviewBorder.BorderBrush   = ParseBrush(_borderColor);
        PreviewTitle.Background     = ParseBrush(_titleColor);
        PreviewTitleText.Foreground = ParseBrush(_textColor);
    }

    private void ApplyThemeNow()
    {
        _manager.ApplyTheme(new ThemeConfig
        {
            GridBackground = _bgColor,
            TitleBarColor  = _titleColor,
            BorderColor    = _borderColor,
            TextColor      = _textColor,
        });
    }

    private static SolidColorBrush ParseBrush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush(Colors.Transparent); }
    }

    // ─── Color Buttons ──────────────────────────────────────────────────────

    private void BgColorBtn_Click(object sender, RoutedEventArgs e)
    {
        if (PickColor(ThemeService.ToDrawingColor(_bgColor), out var c))
        {
            _bgColor = ThemeService.ToHex(c, (byte)BgOpacity.Value);
            SetColorButton(BgColorBtn, _bgColor);
            UpdatePreview();
            ApplyThemeNow();
        }
    }

    private void TitleColorBtn_Click(object sender, RoutedEventArgs e)
    {
        if (PickColor(ThemeService.ToDrawingColor(_titleColor), out var c))
        {
            _titleColor = ThemeService.ToHex(c, (byte)TitleOpacity.Value);
            SetColorButton(TitleColorBtn, _titleColor);
            UpdatePreview();
            ApplyThemeNow();
        }
    }

    private void BorderColorBtn_Click(object sender, RoutedEventArgs e)
    {
        if (PickColor(ThemeService.ToDrawingColor(_borderColor), out var c))
        {
            _borderColor = ThemeService.ToHex(c, (byte)BorderOpacity.Value);
            SetColorButton(BorderColorBtn, _borderColor);
            UpdatePreview();
            ApplyThemeNow();
        }
    }

    private void TextColorBtn_Click(object sender, RoutedEventArgs e)
    {
        if (PickColor(ThemeService.ToDrawingColor(_textColor), out var c))
        {
            _textColor = ThemeService.ToHex(c, (byte)TextOpacity.Value);
            SetColorButton(TextColorBtn, _textColor);
            UpdatePreview();
            ApplyThemeNow();
        }
    }

    private static bool PickColor(System.Drawing.Color initial, out System.Drawing.Color result)
    {
        using var dlg = new WinForms.ColorDialog
        {
            Color         = initial,
            FullOpen      = true,
            AllowFullOpen = true,
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
        {
            result = dlg.Color;
            return true;
        }
        result = initial;
        return false;
    }

    // ─── Opacity Sliders ────────────────────────────────────────────────────

    private void BgOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _bgColor = ThemeService.ToHex(ThemeService.ToDrawingColor(_bgColor), (byte)BgOpacity.Value);
        BgOpacityLabel.Text = $"{(int)BgOpacity.Value}";
        UpdatePreview();
        ApplyThemeNow();
    }

    private void TitleOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _titleColor = ThemeService.ToHex(ThemeService.ToDrawingColor(_titleColor), (byte)TitleOpacity.Value);
        TitleOpacityLabel.Text = $"{(int)TitleOpacity.Value}";
        UpdatePreview();
        ApplyThemeNow();
    }

    private void BorderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _borderColor = ThemeService.ToHex(ThemeService.ToDrawingColor(_borderColor), (byte)BorderOpacity.Value);
        BorderOpacityLabel.Text = $"{(int)BorderOpacity.Value}";
        UpdatePreview();
        ApplyThemeNow();
    }

    private void TextOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _textColor = ThemeService.ToHex(ThemeService.ToDrawingColor(_textColor), (byte)TextOpacity.Value);
        TextOpacityLabel.Text = $"{(int)TextOpacity.Value}";
        UpdatePreview();
        ApplyThemeNow();
    }

    // ─── Spaces ─────────────────────────────────────────────────────────────

    private void LoadSpacesList()
    {
        SpacesList.ItemsSource = null;
        SpacesList.ItemsSource = _manager.Spaces;
    }

    private void OnSpacesChanged() => Dispatcher.Invoke(LoadSpacesList);

    private void SpacesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = SpacesList.SelectedItem is SpaceData;
        RenameSpaceBtn.IsEnabled = hasSelection;
        SwitchSpaceBtn.IsEnabled = hasSelection;
        DeleteSpaceBtn.IsEnabled = hasSelection && _manager.Spaces.Count > 1;
    }

    private void AddSpace_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptText("Neuen Space erstellen", "Space-Name:", "Neuer Space");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var space = _manager.CreateSpace(name);
            SpacesList.SelectedItem = space;
        }
    }

    private void RenameSpace_Click(object sender, RoutedEventArgs e)
    {
        if (SpacesList.SelectedItem is not SpaceData space) return;
        var name = PromptText("Space umbenennen", "Neuer Name:", space.Name);
        if (!string.IsNullOrWhiteSpace(name))
            _manager.RenameSpace(space.Id, name);
    }

    private void SwitchSpace_Click(object sender, RoutedEventArgs e)
    {
        if (SpacesList.SelectedItem is not SpaceData space) return;
        _manager.SwitchSpace(space.Id);
    }

    private void DeleteSpace_Click(object sender, RoutedEventArgs e)
    {
        if (SpacesList.SelectedItem is not SpaceData space) return;
        var r = MessageBox.Show(
            $"Space \"{space.Name}\" und alle seine Grids löschen?",
            "Space löschen?", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r == MessageBoxResult.Yes)
            _manager.DeleteSpace(space.Id);
    }

    // ─── Other ──────────────────────────────────────────────────────────────

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        var result = WinForms.MessageBox.Show(
            "Wirklich alles zurücksetzen?\n\n" +
            "Dies wird:\n" +
            "• Alle Grids löschen\n" +
            "• Farben auf Standard zurücksetzen\n" +
            "• Alle Dateien in Grids wieder auf dem Desktop anzeigen\n\n" +
            "Die App wird nach dem Zurücksetzen neu gestartet.",
            "Alles Zurücksetzen?",
            WinForms.MessageBoxButtons.YesNo,
            WinForms.MessageBoxIcon.Warning);

        if (result == WinForms.DialogResult.Yes)
        {
            try
            {
                _manager.RestoreAllFilesFromStorage();
                _manager.CloseAllGridWindows();
                _manager.ClearAllGrids();
                _manager.ApplyTheme(new ThemeConfig());

                WinForms.MessageBox.Show(
                    "✅ Erfolgreich zurückgesetzt!\n\nDie App wird jetzt neu gestartet.",
                    "Zurückgesetzt",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Information);

                Close();
                var currentPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                System.Diagnostics.Process.Start(currentPath);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                WinForms.MessageBox.Show(
                    $"Fehler beim Zurücksetzen:\n{ex.Message}",
                    "Fehler",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Error);
            }
        }
    }

    private void Autostart_Changed(object sender, RoutedEventArgs e)
    {
        bool isChecked = AutostartCheckbox.IsChecked ?? false;
        if (isChecked) AutostartHelper.EnableAutostart();
        else AutostartHelper.DisableAutostart();
    }

    private void Notifications_Changed(object sender, RoutedEventArgs e)
    {
        bool isChecked = NotificationsCheckbox.IsChecked ?? false;
        _manager.Config.NotificationsEnabled = isChecked;
        _manager.SaveConfig();
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        var updateService = new UpdateService();
        await updateService.CheckForUpdatesAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _manager.SpacesChanged -= OnSpacesChanged;
        base.OnClosed(e);
    }

    private string? PromptText(string title, string label, string defaultValue)
    {
        var dlg = new PromptDialog(title, label, defaultValue) { Owner = this };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
