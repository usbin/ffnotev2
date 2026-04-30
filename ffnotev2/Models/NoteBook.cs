using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ffnotev2.Models;

public partial class NoteBook : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string? processName;

    // 노트북별 격자 스냅 토글 (DB 영속, 기본 false)
    [ObservableProperty]
    private bool snapEnabled;

    // 노트북별 오버레이 초안 (DB 영속, 키 입력마다 저장)
    [ObservableProperty]
    private string overlayDraft = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ObservableCollection<NoteItem> Notes { get; } = new();
    public ObservableCollection<NoteGroup> Groups { get; } = new();
}
