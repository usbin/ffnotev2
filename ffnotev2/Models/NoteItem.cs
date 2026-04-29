using CommunityToolkit.Mvvm.ComponentModel;

namespace ffnotev2.Models;

public enum NoteType { Text, Image, Link }

public partial class NoteItem : ObservableObject
{
    public int Id { get; set; }
    public int NotebookId { get; set; }
    public NoteType Type { get; set; }

    [ObservableProperty] private string? _content;
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width = 220;
    [ObservableProperty] private double _height = 130;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
