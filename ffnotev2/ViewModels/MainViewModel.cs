using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

    public IEnumerable<NoteItem> SelectedNotes =>
        CurrentNotebook?.Notes.Where(n => n.IsSelected) ?? Enumerable.Empty<NoteItem>();

    public void ClearSelection()
    {
        if (CurrentNotebook is null) return;
        foreach (var n in CurrentNotebook.Notes) n.IsSelected = false;
        foreach (var g in CurrentNotebook.Groups) g.IsSelected = false;
    }

    public void SelectOnly(NoteItem note)
    {
        if (CurrentNotebook is null) return;
        foreach (var n in CurrentNotebook.Notes) n.IsSelected = (n == note);
        foreach (var g in CurrentNotebook.Groups) g.IsSelected = false;
    }

    public IEnumerable<NoteGroup> SelectedGroups =>
        CurrentNotebook?.Groups.Where(g => g.IsSelected) ?? Enumerable.Empty<NoteGroup>();

    public void SelectOnlyGroup(NoteGroup group)
    {
        if (CurrentNotebook is null) return;
        foreach (var n in CurrentNotebook.Notes) n.IsSelected = false;
        foreach (var g in CurrentNotebook.Groups) g.IsSelected = (g == group);
    }

    /// <summary>선택된 노트들의 bbox + padding으로 새 그룹을 생성하고 DB에 저장.
    /// 상단은 헤더(22px) + 여백을 확보해 헤더가 노트에 가려지지 않도록 30px 패딩.</summary>
    public NoteGroup? CreateGroupFromSelectedNotes(double padding = 10)
    {
        if (CurrentNotebook is null) return null;
        var selected = SelectedNotes.ToList();
        if (selected.Count == 0) return null;
        const double headerSpace = 30; // 헤더 22px + 여유 8px
        var minX = selected.Min(n => n.X) - padding;
        var minY = selected.Min(n => n.Y) - headerSpace;
        var maxX = selected.Max(n => n.X + n.Width) + padding;
        var maxY = selected.Max(n => n.Y + n.Height) + padding;
        var g = new NoteGroup
        {
            NotebookId = CurrentNotebook.Id,
            X = minX, Y = minY,
            Width = maxX - minX, Height = maxY - minY
        };
        g.Id = _db.AddGroup(g);
        CurrentNotebook.Groups.Add(g);
        App.Undo?.Push(new AddGroupAction(g, CurrentNotebook, _db));
        return g;
    }

    public void DeleteGroup(NoteGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (CurrentNotebook is null) return;
        _db.DeleteGroup(group.Id);
        CurrentNotebook.Groups.Remove(group);
        App.Undo?.Push(new DeleteGroupAction(group, CurrentNotebook, _db));
    }

    public void UpdateGroupPosition(NoteGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        _db.UpdateGroup(group);
    }

    /// <summary>여러 그룹의 멤버 노트들의 합집합. 중복 제거.</summary>
    public IList<NoteItem> GetMemberNotesOf(IEnumerable<NoteGroup> groups)
    {
        if (CurrentNotebook is null) return Array.Empty<NoteItem>();
        var set = new HashSet<NoteItem>();
        foreach (var g in groups)
        {
            var (notes, _) = GetMembersOf(g);
            foreach (var n in notes) set.Add(n);
        }
        return set.ToList();
    }

    /// <summary>그룹의 bbox에 완전 내포된 모든 NoteItem과 NoteGroup(자기 자신 제외) 반환.</summary>
    public (IList<NoteItem> Notes, IList<NoteGroup> Groups) GetMembersOf(NoteGroup g)
    {
        if (CurrentNotebook is null) return (Array.Empty<NoteItem>(), Array.Empty<NoteGroup>());
        bool Inside(double x, double y, double w, double h) =>
            x >= g.X && y >= g.Y && (x + w) <= (g.X + g.Width) && (y + h) <= (g.Y + g.Height);
        var notes = CurrentNotebook.Notes.Where(n => Inside(n.X, n.Y, n.Width, n.Height)).ToList();
        var groups = CurrentNotebook.Groups
            .Where(other => other != g && Inside(other.X, other.Y, other.Width, other.Height))
            .ToList();
        return (notes, groups);
    }

    private DispatcherTimer? _bulkSnapTimer;

    /// <summary>
    /// 현재 노트북의 모든 노트를 일괄 격자 정렬한 뒤, 시작 위치 → 목표 위치/크기까지
    /// 약 300ms ease-out cubic 애니메이션으로 부드럽게 이동.
    /// 충돌 해결: 매 반복마다 "오른쪽으로 빠져나갈 거리"와 "아래로 빠져나갈 거리"를 계산해
    /// 더 짧은 쪽으로 밀어냄 (노트가 멀리 사라지지 않도록).
    /// </summary>
    public void BulkSnap()
    {
        if (CurrentNotebook is null) return;
        if (_bulkSnapTimer is { IsEnabled: true }) return; // 진행 중이면 무시

        var allNotes  = CurrentNotebook.Notes.ToList();
        var allGroups = CurrentNotebook.Groups.ToList();
        if (allNotes.Count == 0 && allGroups.Count == 0) return;

        // ── 1. 최상위 그룹 / 자유 노트 분류 ───────────────────────────────
        // 최상위 그룹: 다른 그룹 bbox에 완전 내포되지 않은 그룹
        bool GroupContains(NoteGroup outer, NoteGroup inner) =>
            inner.X >= outer.X && inner.Y >= outer.Y &&
            inner.X + inner.Width  <= outer.X + outer.Width &&
            inner.Y + inner.Height <= outer.Y + outer.Height;

        var topGroups = allGroups
            .Where(g => !allGroups.Any(o => o != g && GroupContains(o, g)))
            .ToList();

        // 자유 노트: 어떤 그룹에도 완전 내포되지 않는 노트
        var freeNotes = allNotes.Where(n => !allGroups.Any(g =>
            n.X >= g.X && n.Y >= g.Y &&
            n.X + n.Width  <= g.X + g.Width &&
            n.Y + n.Height <= g.Y + g.Height)).ToList();

        // ── 2. 목표 위치 계산 ─────────────────────────────────────────────
        // 자유 노트: X/Y floor, W/H ceil
        var freeNoteTargets = freeNotes.ToDictionary(n => n, n => (
            X: Math.Floor(n.X / GridSize) * GridSize,
            Y: Math.Floor(n.Y / GridSize) * GridSize,
            W: Math.Ceiling(n.Width  / GridSize) * GridSize,
            H: Math.Ceiling(n.Height / GridSize) * GridSize));

        // 최상위 그룹: X/Y floor (W/H 변경 없음 — 멤버십 보존)
        var groupTargetXY = topGroups.ToDictionary(g => g, g => (
            X: Math.Floor(g.X / GridSize) * GridSize,
            Y: Math.Floor(g.Y / GridSize) * GridSize));

        // ── 3. 충돌 해결 (자유 노트 + 최상위 그룹 혼합) ──────────────────
        int fn = freeNotes.Count, gn = topGroups.Count;
        double[] tx = new double[fn + gn], ty = new double[fn + gn],
                 tw = new double[fn + gn], th = new double[fn + gn];

        for (int i = 0; i < fn; i++)
        {
            var t = freeNoteTargets[freeNotes[i]];
            (tx[i], ty[i], tw[i], th[i]) = (t.X, t.Y, t.W, t.H);
        }
        for (int i = 0; i < gn; i++)
        {
            var g = topGroups[i];
            var t = groupTargetXY[g];
            (tx[fn + i], ty[fn + i], tw[fn + i], th[fn + i]) = (t.X, t.Y, g.Width, g.Height);
        }

        var order = Enumerable.Range(0, fn + gn)
            .OrderBy(i => ty[i]).ThenBy(i => tx[i]).ToList();

        var placed = new List<(double X, double Y, double W, double H)>();
        const int hardSafety = 10_000;
        foreach (var idx in order)
        {
            var safety = 0;
            while (true)
            {
                var overlap = placed.Where(p =>
                    RectOverlaps(tx[idx], ty[idx], tw[idx], th[idx], p.X, p.Y, p.W, p.H)).ToList();
                if (overlap.Count == 0) break;
                var ddx = overlap.Max(p => p.X + p.W - tx[idx]);
                var ddy = overlap.Max(p => p.Y + p.H - ty[idx]);
                ddx = Math.Ceiling(ddx / GridSize) * GridSize;
                ddy = Math.Ceiling(ddy / GridSize) * GridSize;
                if (ddx <= ddy) tx[idx] += ddx;
                else ty[idx] += ddy;
                if (++safety > hardSafety) break;
            }
            placed.Add((tx[idx], ty[idx], tw[idx], th[idx]));
        }

        // 충돌 해결 결과 반영
        for (int i = 0; i < fn; i++)
            freeNoteTargets[freeNotes[i]] = (tx[i], ty[i], tw[i], th[i]);
        for (int i = 0; i < gn; i++)
            groupTargetXY[topGroups[i]] = (tx[fn + i], ty[fn + i]);

        // ── 4. 그룹 멤버(노트·하위그룹)에 그룹 delta 전파 ────────────────
        var memberNoteTargets  = new Dictionary<NoteItem,  (double X, double Y, double W, double H)>();
        var memberGroupTargets = new Dictionary<NoteGroup, (double X, double Y)>();

        foreach (var g in topGroups)
        {
            var t = groupTargetXY[g];
            double ddx = t.X - g.X, ddy = t.Y - g.Y;
            if (ddx == 0 && ddy == 0) continue;
            var (mNotes, mGroups) = GetMembersOf(g);
            foreach (var n  in mNotes)  memberNoteTargets[n]   = (n.X + ddx, n.Y + ddy, n.Width, n.Height);
            foreach (var sg in mGroups) memberGroupTargets[sg] = (sg.X + ddx, sg.Y + ddy);
        }

        // ── 5. 애니메이션 ────────────────────────────────────────────────
        var animNotes  = freeNotes.Concat(memberNoteTargets.Keys).ToList();
        var animGroups = topGroups.Concat(memberGroupTargets.Keys).ToList();

        var noteStart  = animNotes.ToDictionary(n => n, n => (n.X, n.Y, n.Width, n.Height));
        var groupStart = animGroups.ToDictionary(g => g, g => (g.X, g.Y));

        var allNoteTargets = new Dictionary<NoteItem, (double X, double Y, double W, double H)>(freeNoteTargets);
        foreach (var kv in memberNoteTargets) allNoteTargets[kv.Key] = kv.Value;

        var allGroupTargets = new Dictionary<NoteGroup, (double X, double Y)>();
        foreach (var g in topGroups)          allGroupTargets[g]  = groupTargetXY[g];
        foreach (var kv in memberGroupTargets) allGroupTargets[kv.Key] = kv.Value;

        var sw       = Stopwatch.StartNew();
        var duration = TimeSpan.FromMilliseconds(100);
        _bulkSnapTimer?.Stop();
        _bulkSnapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _bulkSnapTimer.Tick += (_, _) =>
        {
            var prog = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / duration.TotalMilliseconds);
            foreach (var n in animNotes)
            {
                var s = noteStart[n]; var tg = allNoteTargets[n];
                n.X = s.X + (tg.X - s.X) * prog;
                n.Y = s.Y + (tg.Y - s.Y) * prog;
                n.Width  = s.Width  + (tg.W - s.Width)  * prog;
                n.Height = s.Height + (tg.H - s.Height) * prog;
            }
            foreach (var g in animGroups)
            {
                var s = groupStart[g]; var tg = allGroupTargets[g];
                g.X = s.X + (tg.X - s.X) * prog;
                g.Y = s.Y + (tg.Y - s.Y) * prog;
            }
            if (prog >= 1.0)
            {
                _bulkSnapTimer!.Stop();
                // 부동소수 오차 방지: 마지막에 정확한 목표값으로 확정 + DB 저장
                foreach (var n in animNotes)
                {
                    var tg = allNoteTargets[n];
                    n.X = tg.X; n.Y = tg.Y; n.Width = tg.W; n.Height = tg.H;
                    _db.UpdateNote(n);
                }
                foreach (var g in animGroups)
                {
                    var tg = allGroupTargets[g];
                    g.X = tg.X; g.Y = tg.Y;
                    _db.UpdateGroup(g);
                }
            }
        };
        _bulkSnapTimer.Start();
    }

    private static bool RectOverlaps(double ax, double ay, double aw, double ah,
                                     double bx, double by, double bw, double bh) =>
        ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;

    /// <summary>현재 노트북에서 from을 기준으로 지정 방향에서 가장 가까운 노트 반환 (모든 타입).</summary>
    public NoteItem? FindNeighborNote(NoteItem from, string direction)
    {
        if (CurrentNotebook is null) return null;
        var fromCx = from.X + from.Width / 2;
        var fromCy = from.Y + from.Height / 2;
        NoteItem? best = null;
        double bestScore = double.MaxValue;
        foreach (var n in CurrentNotebook.Notes)
        {
            if (n == from) continue;
            var cx = n.X + n.Width / 2;
            var cy = n.Y + n.Height / 2;
            var dx = cx - fromCx;
            var dy = cy - fromCy;
            bool inDir = direction switch
            {
                "Right" => dx > 0 && Math.Abs(dx) >= Math.Abs(dy),
                "Left"  => dx < 0 && Math.Abs(dx) >= Math.Abs(dy),
                "Down"  => dy > 0 && Math.Abs(dy) >= Math.Abs(dx),
                "Up"    => dy < 0 && Math.Abs(dy) >= Math.Abs(dx),
                _ => false
            };
            if (!inDir) continue;
            var score = dx * dx + dy * dy;
            if (score < bestScore) { bestScore = score; best = n; }
        }
        return best;
    }

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
        // 오버레이 초안: 키 입력마다 저장 (SQLite UPDATE 미만 ms)
        else if (e.PropertyName == nameof(NoteBook.OverlayDraft))
            _db.SetNotebookOverlayDraft(nb.Id, nb.OverlayDraft);
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

    public NoteItem AddTextNote(string text, double x, double y, bool autoEdit = true)
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
            IsEditing = autoEdit
        };
        note.Id = _db.AddNote(note);
        CurrentNotebook.Notes.Add(note);
        App.Undo?.Push(new AddNoteAction(note, CurrentNotebook, _db));
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
        App.Undo?.Push(new AddNoteAction(note, CurrentNotebook, _db));
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
        App.Undo?.Push(new AddNoteAction(note, CurrentNotebook, _db));
        return note;
    }

    /// <summary>드래그/리사이즈/nudge 등 위치·크기 변경을 Undo 스택에 기록.</summary>
    public void RecordTransform(IEnumerable<(ItemSnapshot Old, ItemSnapshot New)> items)
        => App.Undo?.Push(new TransformItemsAction(items, _db));

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
        if (CurrentNotebook is null) return;
        // 이미지 파일은 즉시 삭제하지 않음 (undo로 복원 가능해야 함). 노트북 삭제 시에만 일괄 정리.
        _db.DeleteNote(note.Id);
        CurrentNotebook.Notes.Remove(note);
        App.Undo?.Push(new DeleteNoteAction(note, CurrentNotebook, _db));
    }

    /// <summary>선택된 노트(첫 1개)를 시스템 클립보드에 복사. 텍스트/링크 → SetText, 이미지 → SetImage.</summary>
    public void CopySelectedToClipboard()
    {
        var note = SelectedNotes.FirstOrDefault();
        if (note is null) return;
        try
        {
            switch (note.Type)
            {
                case NoteType.Text:
                case NoteType.Link:
                    Clipboard.SetText(note.Content ?? string.Empty);
                    break;
                case NoteType.Image:
                    if (!File.Exists(note.Content)) return;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(note.Content, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    Clipboard.SetImage(bmp);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"클립보드 복사 실패: {ex.GetType().Name}\n{ex.Message}",
                "ffnote v2", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
                var note = AddImageNote(path, canvasMouseX, canvasMouseY);
                SelectOnly(note);
                return;
            }

            if (dataObject.GetDataPresent(DataFormats.UnicodeText)
                || dataObject.GetDataPresent(DataFormats.Text))
            {
                var text = (dataObject.GetData(DataFormats.UnicodeText)
                            ?? dataObject.GetData(DataFormats.Text)) as string;
                text = text?.Trim();
                if (string.IsNullOrEmpty(text)) return;

                NoteItem note;
                if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    note = AddLinkNote(text, canvasMouseX, canvasMouseY);
                }
                else
                {
                    // 붙여넣기로 만든 텍스트 노트는 편집 모드 아님 — 선택 상태로만 시작
                    note = AddTextNote(text, canvasMouseX, canvasMouseY, autoEdit: false);
                }
                SelectOnly(note);
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
