using CommunityToolkit.Mvvm.ComponentModel;

namespace ffnotev2.Models;

/// <summary>
/// 그룹은 단순 사각형. 멤버십은 동적 — 그룹 bbox에 완전 내포된 NoteItem/NoteGroup이 멤버.
/// 드래그 시작 시점에 멤버를 스냅샷으로 잡아 같은 Δ로 이동.
/// </summary>
public partial class NoteGroup : ObservableObject
{
    public int Id { get; set; }
    public int NotebookId { get; set; }

    [ObservableProperty] private double x;
    [ObservableProperty] private double y;
    [ObservableProperty] private double width = 200;
    [ObservableProperty] private double height = 200;

    // 다중 선택 상태 (DB 미저장 transient)
    [ObservableProperty] private bool isSelected;
}
