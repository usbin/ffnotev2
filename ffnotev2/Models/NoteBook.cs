using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ffnotev2.Models;

public partial class NoteBook : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _processName;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public ObservableCollection<NoteItem> Notes { get; set; } = new();
}
