using System.Windows;
using VscSplitter.App.Services;

namespace VscSplitter.App.Views;

public partial class InputDialog : Window
{
    private InputDialog(string prompt, string initial)
    {
        InitializeComponent();
        PromptLabel.Text = prompt;
        Input.Text = initial;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    /// <summary>Shows a modal text prompt; returns the trimmed text, or null when cancelled.</summary>
    public static string? Ask(Window owner, string prompt, string initial)
    {
        var dialog = new InputDialog(prompt, initial) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Input.Text.Trim() : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Input.Text))
        {
            DialogResult = true;
        }
    }
}
