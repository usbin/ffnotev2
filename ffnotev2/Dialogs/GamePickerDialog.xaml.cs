using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ffnotev2.Services;

namespace ffnotev2.Dialogs;

public partial class GamePickerDialog : Window
{
    private readonly GameDetectionService _gameDetection;

    public string? SelectedProcessName { get; private set; }

    public GamePickerDialog(GameDetectionService gameDetection)
    {
        ArgumentNullException.ThrowIfNull(gameDetection);
        _gameDetection = gameDetection;
        InitializeComponent();
        LoadProcesses();
    }

    private void LoadProcesses()
    {
        ProcessList.ItemsSource = _gameDetection.GetRunningWindowedProcesses()
            .Select(p => new { p.ProcessName, p.WindowTitle })
            .ToList();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadProcesses();

    private void Select_Click(object sender, RoutedEventArgs e) => Confirm();

    private void ProcessList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Confirm();

    private void Confirm()
    {
        if (ProcessList.SelectedItem is null) return;
        var item = ProcessList.SelectedItem;
        SelectedProcessName = item.GetType().GetProperty("ProcessName")?.GetValue(item) as string;
        DialogResult = true;
        Close();
    }
}
