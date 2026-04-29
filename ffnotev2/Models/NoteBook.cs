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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ObservableCollection<NoteItem> Notes { get; } = new();
}
