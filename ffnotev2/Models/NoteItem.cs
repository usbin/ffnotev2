using CommunityToolkit.Mvvm.ComponentModel;

namespace ffnotev2.Models;

public enum NoteType
{
    Text,
    Image,
    Link
}

public partial class NoteItem : ObservableObject
{
    public int Id { get; set; }
    public int NotebookId { get; set; }
    public NoteType Type { get; set; }

    [ObservableProperty]
    private string content = string.Empty;

    [ObservableProperty]
    private double x;

    [ObservableProperty]
    private double y;

    [ObservableProperty]
    private double width = 200;

    [ObservableProperty]
    private double height = 100;

    // 새로 생성된 노트가 자동으로 편집 모드로 진입하도록 알리는 임시 플래그 (DB 미저장)
    [ObservableProperty]
    private bool isEditing;

    // 다중 선택 상태 (DB 미저장 transient)
    [ObservableProperty]
    private bool isSelected;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
