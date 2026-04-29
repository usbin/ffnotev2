using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ffnotev2.Models;
using ffnotev2.Services;

namespace ffnotev2.ViewModels;

public partial class OverlayViewModel : ObservableObject
{
    private static readonly Random Rng = new();

    private readonly MainViewModel _main;
    private readonly SettingsService _settings;
    private NoteBook? _watchedNotebook;
    private bool _suppressDraftSave;

    [ObservableProperty]
    private string quickNoteText = string.Empty;

    [ObservableProperty]
    private string currentNotebookName = "(노트북 없음)";

    public OverlayViewModel(MainViewModel main, SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(settings);
        _main = main;
        _settings = settings;

        // 초기 로드 중에는 저장 트리거 막기 (디스크 → 메모리 단방향)
        _suppressDraftSave = true;
        QuickNoteText = _settings.Settings.OverlayDraft ?? string.Empty;
        _suppressDraftSave = false;

        AttachToCurrentNotebook();
        UpdateNotebookName();
        _main.PropertyChanged += OnMainChanged;
    }

    partial void OnQuickNoteTextChanged(string value)
    {
        if (_suppressDraftSave) return;
        _settings.Settings.OverlayDraft = value ?? string.Empty;
        _settings.Save();
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentNotebook))
        {
            AttachToCurrentNotebook();
            UpdateNotebookName();
        }
    }

    private void AttachToCurrentNotebook()
    {
        if (_watchedNotebook is not null)
            _watchedNotebook.PropertyChanged -= OnNotebookChanged;
        _watchedNotebook = _main.CurrentNotebook;
        if (_watchedNotebook is not null)
            _watchedNotebook.PropertyChanged += OnNotebookChanged;
    }

    private void OnNotebookChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteBook.Name))
            UpdateNotebookName();
    }

    private void UpdateNotebookName()
        => CurrentNotebookName = _main.CurrentNotebook?.Name ?? "(노트북 없음)";

    [RelayCommand]
    private void Submit()
    {
        var text = QuickNoteText.Trim();
        if (string.IsNullOrEmpty(text) || _main.CurrentNotebook is null) return;

        var x = 60 + Rng.NextDouble() * 320;
        var y = 60 + Rng.NextDouble() * 200;
        _main.AddTextNote(text, x, y);
        QuickNoteText = string.Empty;  // 초안 비우기 → settings에도 빈 문자열 저장됨
    }
}
