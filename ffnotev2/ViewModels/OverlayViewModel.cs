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
        // SettingsService는 더 이상 초안 저장에 사용하지 않음 (노트북별 DB 컬럼으로 이동)

        AttachToCurrentNotebook();
        UpdateNotebookName();
        LoadDraftFromCurrentNotebook();
        _main.PropertyChanged += OnMainChanged;
    }

    partial void OnQuickNoteTextChanged(string value)
    {
        if (_suppressDraftSave) return;
        // 현재 노트북의 OverlayDraft에 반영 — MainViewModel이 PropertyChanged 구독해 DB에 저장
        if (_main.CurrentNotebook is { } nb)
            nb.OverlayDraft = value ?? string.Empty;
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentNotebook))
        {
            AttachToCurrentNotebook();
            UpdateNotebookName();
            LoadDraftFromCurrentNotebook();
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

    /// <summary>현재 노트북의 OverlayDraft를 QuickNoteText에 단방향 로드. 저장 트리거 억제.</summary>
    private void LoadDraftFromCurrentNotebook()
    {
        _suppressDraftSave = true;
        QuickNoteText = _main.CurrentNotebook?.OverlayDraft ?? string.Empty;
        _suppressDraftSave = false;
    }

    [RelayCommand]
    private void Submit()
    {
        var text = QuickNoteText.Trim();
        if (string.IsNullOrEmpty(text) || _main.CurrentNotebook is null) return;

        var x = 60 + Rng.NextDouble() * 320;
        var y = 60 + Rng.NextDouble() * 200;
        _main.AddTextNote(text, x, y);
        QuickNoteText = string.Empty;  // 초안 비우기 → 현재 노트북의 OverlayDraft도 빈 문자열로 저장됨
    }
}
