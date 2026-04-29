using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ffnotev2.Models;
using ffnotev2.Services;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace ffnotev2.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly GameDetectionService _gameDetection;

    public ObservableCollection<NoteBook> Notebooks { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentNotebook))]
    private NoteBook? _currentNotebook;

    public bool HasCurrentNotebook => CurrentNotebook != null;

    public MainViewModel(DatabaseService db, GameDetectionService gameDetection)
    {
        _db = db;
        _gameDetection = gameDetection;

        foreach (var nb in _db.LoadAllNotebooks())
            Notebooks.Add(nb);

        if (Notebooks.Count > 0)
            CurrentNotebook = Notebooks[0];

        _gameDetection.UpdateNotebooks(Notebooks);
        _gameDetection.GameDetected += OnGameDetected;
        _gameDetection.Start();
    }

    private void OnGameDetected(object? sender, GameDetectedEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var nb = Notebooks.FirstOrDefault(n => n.Id == e.Notebook.Id);
            if (nb != null) CurrentNotebook = nb;
        });
    }

    [RelayCommand]
    public void AddNotebook()
    {
        var nb = new NoteBook { Name = "새 노트북", CreatedAt = DateTime.Now };
        _db.SaveNotebook(nb);
        Notebooks.Add(nb);
        CurrentNotebook = nb;
        _gameDetection.UpdateNotebooks(Notebooks);
    }

    [RelayCommand]
    public void DeleteNotebook(NoteBook nb)
    {
        _db.DeleteNotebook(nb.Id);
        Notebooks.Remove(nb);
        if (CurrentNotebook == nb)
            CurrentNotebook = Notebooks.FirstOrDefault();
        _gameDetection.UpdateNotebooks(Notebooks);
    }

    public void RenameNotebook(NoteBook nb, string newName)
    {
        nb.Name = newName;
        _db.SaveNotebook(nb);
    }

    public void SetNotebookProcess(NoteBook nb, string processName)
    {
        nb.ProcessName = processName;
        _db.SaveNotebook(nb);
        _gameDetection.UpdateNotebooks(Notebooks);
    }

    public NoteItem AddTextNote(string text, double x, double y)
    {
        if (CurrentNotebook == null) throw new InvalidOperationException("노트북이 선택되지 않았습니다.");
        var item = new NoteItem
        {
            NotebookId = CurrentNotebook.Id,
            Type = NoteType.Text,
            Content = text,
            X = x, Y = y,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _db.SaveNoteItem(item);
        CurrentNotebook.Notes.Add(item);
        return item;
    }

    public NoteItem AddImageNote(BitmapSource bitmap, double x, double y)
    {
        if (CurrentNotebook == null) throw new InvalidOperationException("노트북이 선택되지 않았습니다.");
        var path = SaveImageToFile(bitmap);
        var item = new NoteItem
        {
            NotebookId = CurrentNotebook.Id,
            Type = NoteType.Image,
            Content = path,
            X = x, Y = y,
            Width = Math.Min(bitmap.PixelWidth, 400),
            Height = Math.Min(bitmap.PixelHeight, 300),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _db.SaveNoteItem(item);
        CurrentNotebook.Notes.Add(item);
        return item;
    }

    public NoteItem AddLinkNote(string url, double x, double y)
    {
        if (CurrentNotebook == null) throw new InvalidOperationException("노트북이 선택되지 않았습니다.");
        var item = new NoteItem
        {
            NotebookId = CurrentNotebook.Id,
            Type = NoteType.Link,
            Content = url,
            X = x, Y = y,
            Width = 300, Height = 56,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _db.SaveNoteItem(item);
        CurrentNotebook.Notes.Add(item);
        return item;
    }

    public void DeleteNoteItem(NoteItem item)
    {
        if (item.Type == NoteType.Image && item.Content != null && File.Exists(item.Content))
            File.Delete(item.Content);
        _db.DeleteNoteItem(item.Id);
        CurrentNotebook?.Notes.Remove(item);
    }

    public void UpdateNoteContent(NoteItem item)
    {
        _db.SaveNoteItem(item);
    }

    public void UpdateNotePosition(NoteItem item)
    {
        _db.UpdateNotePosition(item.Id, item.X, item.Y);
    }

    private static string SaveImageToFile(BitmapSource bitmap)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ffnotev2", "images");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{Guid.NewGuid():N}.png");
        using var stream = File.OpenWrite(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        return path;
    }
}
