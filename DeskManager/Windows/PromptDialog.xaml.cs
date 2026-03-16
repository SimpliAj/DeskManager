using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DeskManager.Windows;

public partial class PromptDialog : Window
{
    public string? Result { get; private set; }

    public PromptDialog(string title, string label, string defaultValue = "")
    {
        InitializeComponent();
        Title          = title;
        LabelText.Text = label;
        InputBox.Text  = defaultValue;
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        Result = InputBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  { Result = InputBox.Text.Trim(); DialogResult = true; }
        if (e.Key == Key.Escape) { DialogResult = false; }
    }
}
