using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace ffnotev2.Dialogs;

public partial class InsertTableDialog : Window
{
    public int Rows { get; private set; } = 3;
    public int Cols { get; private set; } = 3;

    public InsertTableDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => RowsBox.SelectAll();
    }

    public string BuildMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append('|');
        for (int c = 0; c < Cols; c++) sb.Append("   |");
        sb.Append('\n').Append('|');
        for (int c = 0; c < Cols; c++) sb.Append("---|");
        sb.Append('\n');
        for (int r = 0; r < Rows; r++)
        {
            sb.Append('|');
            for (int c = 0; c < Cols; c++) sb.Append("   |");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private void Numeric_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RowsBox.Text, out var r) || r < 1) r = 1;
        if (!int.TryParse(ColsBox.Text, out var c) || c < 1) c = 1;
        Rows = System.Math.Min(r, 50);
        Cols = System.Math.Min(c, 20);
        DialogResult = true;
        Close();
    }
}
