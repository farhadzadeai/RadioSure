using System.Windows;

namespace RadioClassic.Views;

/// <summary>Small single-line input dialog.</summary>
public partial class InputWindow : Window
{
    public string Value => ValueBox.Text.Trim();

    public InputWindow(string title, string prompt, string initial = "")
    {
        InitializeComponent();
        ThemeServiceFixer.Apply(this);
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initial;
        ValueBox.SelectAll();
        ValueBox.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
