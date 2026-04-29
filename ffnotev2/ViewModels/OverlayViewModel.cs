using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ffnotev2.ViewModels;

public partial class OverlayViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private string _quickNoteText = string.Empty;
    [ObservableProperty] private string _currentNotebookName = "노트북 없음";

    public OverlayViewModel(MainViewModel main)
    {
        _main = main;
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentNotebook))
                CurrentNotebookName = main.CurrentNotebook?.Name ?? "노트북 없음";
        };
        CurrentNotebookName = main.CurrentNotebook?.Name ?? "노트북 없음";
    }

    [RelayCommand]
    public void Submit()
    {
        var text = QuickNoteText.Trim();
        if (string.IsNullOrEmpty(text) || _main.CurrentNotebook == null) return;

        var x = 60 + Random.Shared.Next(0, 160);
        var y = 60 + Random.Shared.Next(0, 100);
        _main.AddTextNote(text, x, y);
        QuickNoteText = string.Empty;
    }
}
