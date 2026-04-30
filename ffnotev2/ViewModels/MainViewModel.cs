using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ffnotev2.Models;
using ffnotev2.Services;
using Clipboard = System.Windows.Clipboard;

namespace ffnotev2.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public const double GridSize = 10;

    /// <summary>월드 좌표를 10px 격자에 가장 가까운 값으로 라운딩.</summary>
    public static double Snap(double v) => Math.Round(v / GridSize) * GridSize;

    /// <summary>현재 노트북의 SnapEnabled 토글에 따라 조건부로 스냅. 토글 OFF면 원본 값.</summary>
    public double MaybeSnap(double v) => CurrentNotebook?.SnapEnabled == true ? Snap(v) : v;

    private readonly DatabaseService _db;
    private readonly GameDetectionService _gameDetection;

    public ObservableCollection<NoteBook> Notebooks { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentNotebook))]
    private NoteBook? currentNotebook;

    public bool HasCurrentNotebook => CurrentNotebook is not null;

    public MainViewModel(DatabaseService db, GameDetectionService gameDetection)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(gameDetection);
        _db = db;
        _gameDetection = gameDetection;

        LoadNotebooks();

        _gameDetection.RegisteredProcessNamesProvider = () =>
            Notebooks.Select(n => n.ProcessName ?? string.Empty);
        _gameDetection.GameDetected += OnGameDetected;
    }

    private void LoadNotebooks()
    {
        Notebooks.Clear();
        foreach (var nb in _db.GetNotebooks())
        {
            Notebooks.Add(nb);
            HookNotebook(nb);
        }
        if (CurrentNotebook is null && Notebooks.Count > 0)
            CurrentNotebook = Notebooks[0];
    }

    private void HookNotebook(NoteBook nb)
    {
        foreach (var note in nb.Notes) HookNote(note);
        nb.Notes.CollectionChanged += OnNotesCollectionChanged;
        nb.PropertyChanged += OnNotebookPropertyChanged;
    }

    private void OnNotebookPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NoteBook nb) return;
        // 격자 스냅 토글은 즉시 DB에 영속화
        if (e.PropertyName == nameof(NoteBook.SnapEnabled))
            _db.SetNotebookSnapEnabled(nb.Id, nb.SnapEnabled);
    }

    private void OnNotesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (NoteItem n in e.NewItems) HookNote(n);
        if (e.OldItems is not null)
            foreach (NoteItem n in e.OldItems) UnhookNote(n);
    }

    private void HookNote(NoteItem note) => note.PropertyChanged += OnNotePropertyChanged;
    private void UnhookNote(NoteItem note) => note.PropertyChanged -= OnNotePropertyChanged;

    private void OnNotePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NoteItem note) return;
        // 키 입력마다 Content가 갱신되면 즉시 DB에 반영. 닫기/종료 직전 데이터 유실 방지.
        if (e.PropertyName == nameof(NoteItem.Content))
            _db.UpdateNote(note);
    }

    private void OnGameDetected(object? sender, GameDetectedEventArgs e)
    {
        var matched = Notebooks.FirstOrDefault(n =>
            !string.IsNullOrEmpty(n.ProcessName) &&
            string.Equals(n.ProcessName, e.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (matched is null) return;

        Application.Current?.Dispatcher.BeginInvoke(() => CurrentNotebook = matched);
    }

    [RelayCommand]
    private void CreateNotebook()
    {
        var name = $"노트북 {Notebooks.Count + 1}";
        var id = _db.CreateNotebook(name);
        var nb = new NoteBook { Id = id, Name = name };
        Notebooks.Add(nb);
        HookNotebook(nb);
        CurrentNotebook = nb;
    }

    public void RenameNotebook(NoteBook notebook, string newName)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        if (string.IsNullOrWhiteSpace(newName)) return;
        notebook.Name = newName;
        _db.RenameNotebook(notebook.Id, newName);
    }

    public void SetNotebookProcess(NoteBook notebook, string? processName)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        notebook.ProcessName = processName;
        _db.SetNotebookProcess(notebook.Id, processName);
    }

    public void DeleteNotebook(NoteBook notebook)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        _db.DeleteNotebook(notebook.Id);
        // 이미지 파일 정리
        foreach (var note in notebook.Notes.Where(n => n.Type == NoteType.Image))
            TryDeleteFile(note.Content);
        Notebooks.Remove(notebook);
        if (CurrentNotebook == notebook)
            CurrentNotebook = Notebooks.FirstOrDefault();
    }

    public NoteItem AddTextNote(string text, double x, double y)
    {
        if (CurrentNotebook is null) throw new InvalidOperationException("CurrentNotebook이 없습니다.");
        var note = new NoteItem
        {
            NotebookId = CurrentNotebook.Id,
            Type = NoteType.Text,
            Content = text,
            X = MaybeSnap(x),
            Y = MaybeSnap(y),
            Width = 200,
            Height = 100,
            IsEditing = true
        };
        note.Id = _db.AddNote(note);
        CurrentNotebook.Notes.Add(note);
        return note;
    }

    public NoteItem AddImageNote(string imagePath, double x, double y)
    {
        if (CurrentNotebook is null) throw new InvalidOperationException("CurrentNotebook이 없습니다.");
        var note = new NoteItem
        {
            NotebookId = CurrentNotebook.Id,
            Type = NoteType.Image,
            Content = imagePath,
            X = MaybeSnap(x),
            Y = MaybeSnap(y),
            Width = 400,
            Height = 300
        };
        note.Id = _db.AddNote(note);
        CurrentNotebook.Notes.Add(note);
        return note;
    }

    public NoteItem AddLinkNote(string url, double x, double y)
    {
        if (CurrentNotebook is null) throw new InvalidOperationException("CurrentNotebook이 없습니다.");
        var note = new NoteItem
        {
            NotebookId = CurrentNotebook.Id,
            Type = NoteType.Link,
            Content = url,
            X = MaybeSnap(x),
            Y = MaybeSnap(y),
            Width = 300,
            Height = 56
        };
        note.Id = _db.AddNote(note);
        CurrentNotebook.Notes.Add(note);
        return note;
    }

    public void UpdateNoteContent(NoteItem note)
    {
        ArgumentNullException.ThrowIfNull(note);
        _db.UpdateNote(note);
    }

    public void UpdateNotePosition(NoteItem note)
    {
        ArgumentNullException.ThrowIfNull(note);
        _db.UpdateNotePosition(note.Id, note.X, note.Y);
    }

    public void DeleteNote(NoteItem note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (note.Type == NoteType.Image)
            TryDeleteFile(note.Content);
        _db.DeleteNote(note.Id);
        CurrentNotebook?.Notes.Remove(note);
    }

    public void PasteFromClipboard(double canvasMouseX, double canvasMouseY)
    {
        if (CurrentNotebook is null) return;

        try
        {
            var dataObject = Clipboard.GetDataObject();
            if (dataObject is null) return;

            var bmp = TryGetClipboardImage(dataObject);
            if (bmp is not null)
            {
                var path = SaveImageToFile(bmp);
                AddImageNote(path, canvasMouseX, canvasMouseY);
                return;
            }

            if (dataObject.GetDataPresent(DataFormats.UnicodeText)
                || dataObject.GetDataPresent(DataFormats.Text))
            {
                var text = (dataObject.GetData(DataFormats.UnicodeText)
                            ?? dataObject.GetData(DataFormats.Text)) as string;
                text = text?.Trim();
                if (string.IsNullOrEmpty(text)) return;

                if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    AddLinkNote(text, canvasMouseX, canvasMouseY);
                }
                else
                {
                    AddTextNote(text, canvasMouseX, canvasMouseY);
                }
                return;
            }

            // 이미지도 텍스트도 못 찾았으면 사용자에게 알려주기
            var formats = string.Join(", ", dataObject.GetFormats());
            MessageBox.Show(
                $"클립보드에 알 수 있는 데이터가 없습니다.\n사용 가능한 포맷:\n{formats}",
                "ffnote v2 — 붙여넣기",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"붙여넣기 실패: {ex.GetType().Name}\n{ex.Message}",
                "ffnote v2", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static BitmapSource? TryGetClipboardImage(System.Windows.IDataObject d)
    {
        // 1) PNG 바이트 (Snipping Tool, 브라우저 등이 자주 사용)
        try
        {
            if (d.GetDataPresent("PNG"))
            {
                if (d.GetData("PNG") is MemoryStream ms)
                {
                    ms.Position = 0;
                    var decoder = new PngBitmapDecoder(ms,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count > 0)
                    {
                        var frame = decoder.Frames[0];
                        frame.Freeze();
                        return frame;
                    }
                }
            }
        }
        catch { }

        // 2) DeviceIndependentBitmap (Windows 표준)
        try
        {
            if (d.GetDataPresent(DataFormats.Dib))
            {
                if (d.GetData(DataFormats.Dib) is MemoryStream ms)
                {
                    var bs = DibToBitmapSource(ms);
                    if (bs is not null) return bs;
                }
            }
        }
        catch { }

        // 3) Clipboard.GetImage() — DIB → BitmapSource (가장 자주 동작)
        try
        {
            if (Clipboard.ContainsImage())
            {
                var bs = Clipboard.GetImage();
                if (bs is not null)
                {
                    bs.Freeze();
                    return bs;
                }
            }
        }
        catch { }

        // 4) Bitmap 직접 (드물지만 가능)
        try
        {
            if (d.GetDataPresent(DataFormats.Bitmap))
            {
                if (d.GetData(DataFormats.Bitmap) is BitmapSource bs)
                {
                    if (!bs.IsFrozen) bs.Freeze();
                    return bs;
                }
            }
        }
        catch { }

        return null;
    }

    private static BitmapSource? DibToBitmapSource(MemoryStream dibStream)
    {
        // DIB 헤더 앞에 BMP 파일 헤더(14바이트)를 붙여서 BMP로 디코드
        var dib = dibStream.ToArray();
        if (dib.Length < 40) return null; // BITMAPINFOHEADER 최소 크기

        var fileSize = 14 + dib.Length;
        var dataOffset = 14 + BitConverter.ToInt32(dib, 0); // biSize → 픽셀 데이터 시작

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write(0);
        bw.Write(dataOffset);
        bw.Write(dib);
        ms.Position = 0;

        try
        {
            var decoder = BitmapDecoder.Create(ms,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count > 0)
            {
                var frame = decoder.Frames[0];
                frame.Freeze();
                return frame;
            }
        }
        catch { }
        return null;
    }

    private string SaveImageToFile(BitmapSource bitmap)
    {
        Directory.CreateDirectory(_db.ImagesDirectory);
        var path = Path.Combine(_db.ImagesDirectory, $"{Guid.NewGuid():N}.png");
        using var stream = File.OpenWrite(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        return path;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch
        {
            // 파일 삭제 실패는 조용히 무시
        }
    }
}
