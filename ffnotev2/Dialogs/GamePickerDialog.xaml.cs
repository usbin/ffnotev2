using ffnotev2.Services;
using System.Windows;
using System.Windows.Controls;

namespace ffnotev2.Dialogs;

public partial class GamePickerDialog : Window
{
    public string? SelectedProcessName { get; private set; }

    public GamePickerDialog()
    {
        InitializeComponent();
        LoadProcesses();
    }

    private void LoadProcesses()
    {
        var processes = GameDetectionService.GetRunningWindowedProcesses();
        ProcessList.ItemsSource = processes;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadProcesses();

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessList.SelectedItem is ProcessInfo info)
        {
            SelectedProcessName = info.ProcessName;
            DialogResult = true;
        }
        else
        {
            MessageBox.Show("프로세스를 선택해주세요.", "ffnote v2", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ProcessList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ProcessList.SelectedItem is ProcessInfo info)
        {
            SelectedProcessName = info.ProcessName;
            DialogResult = true;
        }
    }
}
