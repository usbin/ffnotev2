using System.Windows;

namespace ffnotev2.Dialogs;

public partial class RenameDialog : Window
{
    public string EnteredName => NameBox.Text.Trim();

    public RenameDialog(string initialName)
    {
        InitializeComponent();
        NameBox.Text = initialName ?? string.Empty;
        Loaded += (_, _) => NameBox.SelectAll();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EnteredName)) return;
        DialogResult = true;
        Close();
    }
}
