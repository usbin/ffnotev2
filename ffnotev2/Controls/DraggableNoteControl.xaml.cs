using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using ffnotev2.Models;
using ffnotev2.Services;
using Application = System.Windows.Application;
using Point = System.Windows.Point;

namespace ffnotev2.Controls;

public partial class DraggableNoteControl : UserControl
{
    private TableGridAdorner? _tableAdorner;
    private bool _isDragging;
    private Point _dragStart;
    // 일괄 드래그를 위해 선택된 모든 노트의 시작 좌표 캡처
    private List<(NoteItem Item, double StartX, double StartY)> _dragGroup = new();
    // 다중 선택에 그룹이 포함된 경우: 선택된 그룹+멤버하위그룹 / 멤버노트 중 _dragGroup에 없는 것
    private List<(NoteGroup Group, double StartX, double StartY)> _groupDragSnap = new();
    private List<(NoteItem Item, double StartX, double StartY)> _memberNoteDragSnap = new();
    // 본문 드래그 후보 상태: MouseDown 시 set → MouseMove에서 임계값 초과 시 실제 드래그로 승격
    private bool _bodyPotentialDrag;
    private Point _bodyDragStartScreen;
    // 일괄 리사이즈를 위해 선택된 모든 노트의 시작 X/Y/W/H 캡처 (좌/상 엣지가 X/Y도 변경)
    private List<(NoteItem Item, double StartX, double StartY, double StartW, double StartH)> _resizeGroup = new();
    private string _resizeEdge = "Corner";

    public DraggableNoteControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private static SettingsService? CurrentSettings =>
        (Application.Current as App)?.SettingsService;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is NoteItem old) old.PropertyChanged -= OnItemPropertyChanged;
        if (e.NewValue is NoteItem fresh) fresh.PropertyChanged += OnItemPropertyChanged;
        // RefreshDocument는 Loaded 시점에서만 호출 — DataContextChanged 시점은 아직 visual tree
        // 부착 전이라 FlowDocument 할당이 불안정할 수 있음. Loaded 이후 PropertyChanged(Content)로 추적.
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteItem.IsEditing) && Item?.IsEditing == true)
        {
            // 외부에서 IsEditing=true 설정 시 (방향키 이동, Enter 등) 이미 로드된 컨트롤도 편집 모드로 진입
            // 텍스트 외 타입은 BeginEdit를 무시하지만 플래그는 항상 리셋해 stuck 방지
            Item.IsEditing = false;
            if (Item.Type == NoteType.Text) BeginEdit();
        }
        else if (e.PropertyName == nameof(NoteItem.Content))
        {
            // 표시 모드일 때만 갱신 (편집 중엔 매 키스트로크마다 다시 그릴 필요 없음)
            if (EditorContainer.Visibility != Visibility.Visible) RefreshDocument();
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ApplyEditorFont();
        RefreshDocument();
        // 편집 중이면 줄 번호 표시도 즉시 갱신
        if (EditorContainer.Visibility == Visibility.Visible)
        {
            SyncLineNumbersVisibility();
            UpdateLineNumbers();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (CurrentSettings is { } svc) svc.SettingsChanged -= OnSettingsChanged;
        App.MainVM.QueryResultsInvalidated -= OnQueryInvalidated;
    }

    private void OnQueryInvalidated(object? sender, EventArgs e)
    {
        // ```sql 펜스를 포함하는 텍스트 노트만 다시 그림 (비용 절감)
        if (Item is null || Item.Type != NoteType.Text) return;
        if (EditorContainer.Visibility == Visibility.Visible) return;  // 편집 중이면 표시 갱신 불필요
        var c = Item.Content ?? string.Empty;
        if (c.IndexOf("```sql", StringComparison.OrdinalIgnoreCase) < 0) return;
        RefreshDocument();
    }

    private void RefreshDocument()
    {
        if (Item is null || Item.Type != NoteType.Text) return;
        var settings = CurrentSettings?.Settings;
        var fontFamily = settings?.NoteFontFamily ?? "Segoe UI";
        var fontSize = settings?.NoteFontSize ?? 13;
        var imageBase = App.MainVM.ImagesDirectory;
        TextDisplayScroll.Document = MarkdownRenderer.Render(Item.Content ?? string.Empty, fontFamily, fontSize, imageBase);
    }

    private void ApplyEditorFont()
    {
        var settings = CurrentSettings?.Settings;
        if (settings is null) return;
        var family = settings.EditorMonospace
            ? "Consolas, Cascadia Mono, Courier New"
            : settings.NoteFontFamily;
        TextEditor.FontFamily = new System.Windows.Media.FontFamily(family);
        TextEditor.FontSize = settings.NoteFontSize;
        // 줄 번호 폰트는 UpdateLineNumbers에서 매번 TextEditor 폰트로 동기화
        UpdateLineNumbers();
    }

    private NoteItem? Item => DataContext as NoteItem;

    private static bool IsInsideTextBox(object? source)
    {
        DependencyObject? d = source as DependencyObject;
        while (d is not null)
        {
            if (d is TextBox) return true;
            d = VisualTreeWalker.GetAnyParent(d);
        }
        return false;
    }

    private Canvas? FindParentCanvas()
    {
        DependencyObject? cur = this;
        while (cur is not null)
        {
            cur = VisualTreeWalker.GetAnyParent(cur);
            if (cur is Canvas canvas) return canvas;
        }
        return null;
    }

    // 드래그 시작 시 이동 대상 스냅샷 빌드.
    // _dragGroup: 선택된 자유 노트(그룹 멤버 아닌 것) + 리더(항상 포함 — snap delta 계산용)
    // _groupDragSnap: 선택된 그룹 + 그 멤버 하위그룹
    // _memberNoteDragSnap: 그룹 멤버 노트 중 _dragGroup에 없는 것 (독립 선택 안 된 멤버)
    private void BuildDragSnapshots()
    {
        if (Item is null) return;
        var selNotes  = App.MainVM.SelectedNotes.ToList();
        var selGroups = App.MainVM.SelectedGroups.ToList();

        var seenG = new HashSet<Models.NoteGroup>();
        _groupDragSnap = new();
        var memberNotes = new HashSet<NoteItem>();

        foreach (var g in selGroups)
        {
            if (!seenG.Add(g)) continue;
            _groupDragSnap.Add((g, g.X, g.Y));
            var (mNotes, mGroups) = App.MainVM.GetMembersOf(g);
            foreach (var sg in mGroups) if (seenG.Add(sg)) _groupDragSnap.Add((sg, sg.X, sg.Y));
            foreach (var n in mNotes) memberNotes.Add(n);
        }

        // 자유 노트: 선택됐지만 그룹 멤버 아닌 것 + 리더(항상 포함)
        var noteList = selNotes.Contains(Item) ? selNotes : new List<NoteItem> { Item };
        _dragGroup = noteList
            .Where(n => !memberNotes.Contains(n) || ReferenceEquals(n, Item))
            .Select(n => (n, n.X, n.Y)).ToList();
        if (!_dragGroup.Any(g => ReferenceEquals(g.Item, Item)))
            _dragGroup.Insert(0, (Item, Item.X, Item.Y));

        // 그룹 멤버 노트 중 _dragGroup에 없는 것
        var inDrag = new HashSet<NoteItem>(_dragGroup.Select(g => g.Item));
        _memberNoteDragSnap = memberNotes
            .Where(n => !inDrag.Contains(n))
            .Select(n => (n, n.X, n.Y)).ToList();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Item is null) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;

        _isDragging = true;
        _dragStart = e.GetPosition(canvas);
        BuildDragSnapshots();

        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || Item is null) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;

        var pos = e.GetPosition(canvas);
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;

        // 다중 선택 + 격자 스냅 ON에서 각 노트를 독립 스냅하면 시작 위치가 비정렬일 때
        // 노트마다 다른 격자로 라운딩돼 상대 거리가 한 칸씩 어긋남. 클릭한 노트(Item)를
        // leader로 잡아 snap된 실제 delta를 산출하고, 같은 delta를 그룹 전체에 적용.
        var leader = _dragGroup.FirstOrDefault(g => ReferenceEquals(g.Item, Item));
        if (leader.Item is null) leader = _dragGroup[0];
        var newLeaderX = App.MainVM.MaybeSnap(leader.StartX + dx);
        var newLeaderY = App.MainVM.MaybeSnap(leader.StartY + dy);
        var actualDx = newLeaderX - leader.StartX;
        var actualDy = newLeaderY - leader.StartY;

        foreach (var (it, sx, sy) in _dragGroup)
        {
            it.X = sx + actualDx;
            it.Y = sy + actualDy;
        }
        foreach (var (g, sx, sy) in _groupDragSnap)  { g.X = sx + actualDx; g.Y = sy + actualDy; }
        foreach (var (n, sx, sy) in _memberNoteDragSnap) { n.X = sx + actualDx; n.Y = sy + actualDy; }
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        // 변경된 항목만 Undo 스택에 등록 (실제 위치가 바뀐 경우만)
        var changes = new List<(Services.ItemSnapshot Old, Services.ItemSnapshot New)>();
        foreach (var (it, sx, sy) in _dragGroup)
            if (it.X != sx || it.Y != sy)
                changes.Add((new Services.ItemSnapshot(it, sx, sy, it.Width, it.Height),
                             new Services.ItemSnapshot(it, it.X, it.Y, it.Width, it.Height)));
        foreach (var (g, sx, sy) in _groupDragSnap)
            if (g.X != sx || g.Y != sy)
                changes.Add((new Services.ItemSnapshot(g, sx, sy, g.Width, g.Height),
                             new Services.ItemSnapshot(g, g.X, g.Y, g.Width, g.Height)));
        foreach (var (n, sx, sy) in _memberNoteDragSnap)
            if (n.X != sx || n.Y != sy)
                changes.Add((new Services.ItemSnapshot(n, sx, sy, n.Width, n.Height),
                             new Services.ItemSnapshot(n, n.X, n.Y, n.Width, n.Height)));

        foreach (var (it, _, _) in _dragGroup)          App.MainVM.UpdateNotePosition(it);
        foreach (var (g,  _, _) in _groupDragSnap)      App.MainVM.UpdateGroupPosition(g);
        foreach (var (n,  _, _) in _memberNoteDragSnap) App.MainVM.UpdateNotePosition(n);

        if (changes.Count > 0) App.MainVM.RecordTransform(changes);
        _dragGroup.Clear();
        _groupDragSnap.Clear();
        _memberNoteDragSnap.Clear();
    }

    // 본문 영역 클릭 여부 — OriginalSource에서 BodyArea(콘텐츠 Grid) 부모로 거슬러 올라가 확인.
    // 타이틀바 Border와 리사이즈 Thumb은 BodyArea 외부이므로 false 반환.
    private bool IsOnBodyArea(object? source)
    {
        DependencyObject? d = source as DependencyObject;
        while (d is not null)
        {
            if (ReferenceEquals(d, BodyArea)) return true;
            d = VisualTreeWalker.GetAnyParent(d);
        }
        return false;
    }

    private void TextDisplay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // FlowDocumentScrollViewer 자체 selection 시작 차단 — 단순 클릭에서 마지막 글자가
        // 자동 선택되어 파란 하이라이트 잔존하는 문제 회피. 본문 드래그/노트 선택은 부모
        // UserControl_PreviewMouseLeftButtonDown(이미 fire됨)이 처리하므로 자식 라우팅 차단해도 OK.
        // 표시 모드의 텍스트 선택/복사는 정책상 비활성(이전 TextBlock 동작과 동일).
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            // 라우팅 도중 Visibility 토글이 후속 라우팅을 깨뜨릴 수 있어 다음 사이클로 미룸
            Dispatcher.BeginInvoke(new Action(BeginEdit), DispatcherPriority.Input);
        }
    }

    private void BeginEdit()
    {
        if (Item is null || Item.Type != NoteType.Text) return;
        // 표시 모드 → 편집 모드로 스왑 (IsReadOnly 토글 회피로 한글 IME 정상 동작)
        TextDisplayScroll.Visibility = Visibility.Collapsed;
        EditorContainer.Visibility = Visibility.Visible;
        SyncLineNumbersVisibility();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Keyboard.Focus(TextEditor);
            TextEditor.CaretIndex = TextEditor.Text.Length;
            HookEditorScroll();
            UpdateLineNumbers();  // TextBox measure 끝난 후 LineCount 정확
            AttachTableAdorner();
        }), DispatcherPriority.Loaded);
    }

    private void AttachTableAdorner()
    {
        if (_tableAdorner is not null) return;
        var layer = AdornerLayer.GetAdornerLayer(TextEditor);
        if (layer is null) return;
        _tableAdorner = new TableGridAdorner(TextEditor);
        layer.Add(_tableAdorner);
    }

    private void DetachTableAdorner()
    {
        if (_tableAdorner is null) return;
        var layer = AdornerLayer.GetAdornerLayer(TextEditor);
        layer?.Remove(_tableAdorner);
        _tableAdorner = null;
    }

    private void SyncLineNumbersVisibility()
    {
        var settings = CurrentSettings?.Settings;
        bool show = settings?.ShowLineNumbers == true;
        LineNumberColumn.Width = show ? new GridLength(40) : new GridLength(0);
        LineNumberBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// TextBox의 각 visual line(wrap 포함)에 대해 GetRectFromCharacterIndex로 정확한 Y 좌표를 얻고
    /// 그 위치에 TextBlock을 절대 배치. LineHeight 불일치로 인한 누적 어긋남 없음.
    /// 스크롤 오프셋도 직접 반영.
    /// </summary>
    private void UpdateLineNumbers()
    {
        if (LineNumberColumn.Width.Value <= 0) return;
        if (TextEditor.ActualHeight <= 0) return;

        LineNumberCanvas.Children.Clear();
        var t = TextEditor.Text ?? string.Empty;
        int visualLines = TextEditor.LineCount;
        if (visualLines <= 0) visualLines = 1;

        double scrollOffset = _editorScrollViewer?.VerticalOffset ?? 0;

        int logical = 1;
        for (int v = 0; v < visualLines; v++)
        {
            int charIdx;
            try { charIdx = TextEditor.GetCharacterIndexFromLineIndex(v); }
            catch { continue; }

            bool isLogicalStart = v == 0
                || (charIdx > 0 && charIdx <= t.Length && t[charIdx - 1] == '\n');

            if (isLogicalStart)
            {
                Rect rect;
                try { rect = TextEditor.GetRectFromCharacterIndex(charIdx); }
                catch { logical++; continue; }
                if (double.IsInfinity(rect.Y) || double.IsNaN(rect.Y)) { logical++; continue; }

                // GetRectFromCharacterIndex는 TextBox 콘텐츠 좌표(스크롤 오프셋 이미 반영된 visual 좌표).
                double y = rect.Y;
                // 가시 영역 안만 그림 (스크롤 밖은 스킵)
                if (y < -rect.Height || y > TextEditor.ActualHeight) { logical++; continue; }

                var tb = new TextBlock
                {
                    Text = logical.ToString(),
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x77, 0x77, 0x77)),
                    FontFamily = TextEditor.FontFamily,
                    FontSize = TextEditor.FontSize,
                    TextAlignment = TextAlignment.Right,
                    Width = LineNumberColumn.Width.Value - 6,
                };
                Canvas.SetLeft(tb, 0);
                Canvas.SetTop(tb, y);
                LineNumberCanvas.Children.Add(tb);
                logical++;
            }
        }
    }

    private void TextEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateLineNumbers();
            _tableAdorner?.InvalidateVisual();
        }), DispatcherPriority.Loaded);
        ScheduleAlignTable();
    }

    // 표 자동 정렬: 캐럿이 표 행 안일 때 컬럼별 max display width로 모든 행을 공백 패딩.
    // 매 키 직후 즉시 동작하면 한글 IME 합성 중 Text 교체로 자모 분리 위험 → 300ms 디바운스 + Background priority.
    private DispatcherTimer? _alignTimer;
    private bool _suppressAlign;

    private void ScheduleAlignTable()
    {
        if (_suppressAlign) return;
        if (_alignTimer is null)
        {
            _alignTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _alignTimer.Tick += (_, _) =>
            {
                _alignTimer!.Stop();
                AlignTableAtCaret();
            };
        }
        _alignTimer.Stop();
        _alignTimer.Start();
    }

    private void AlignTableAtCaret()
    {
        if (EditorContainer.Visibility != Visibility.Visible) return;
        var t = TextEditor.Text ?? string.Empty;
        if (t.Length == 0) return;
        int caret = TextEditor.CaretIndex;
        int ls = LineStart(t, caret);
        int le = LineEnd(t, caret);
        if (!IsTableRowLine(t, ls, le)) return;

        // 표 영역 — 위/아래로 표 행 확장
        int tableStart = ls;
        while (tableStart > 0)
        {
            int prevEnd = tableStart - 1;
            if (prevEnd < 0 || t[prevEnd] != '\n') break;
            int prevStart = LineStart(t, prevEnd > 0 ? prevEnd - 1 : 0);
            if (!IsTableRowLine(t, prevStart, prevEnd)) break;
            tableStart = prevStart;
        }
        int tableEnd = le;
        while (tableEnd < t.Length)
        {
            if (t[tableEnd] != '\n') break;
            int nextStart = tableEnd + 1;
            if (nextStart > t.Length) break;
            int nextEnd = LineEnd(t, nextStart);
            if (!IsTableRowLine(t, nextStart, nextEnd)) break;
            tableEnd = nextEnd;
        }

        var tableText = t.Substring(tableStart, tableEnd - tableStart);
        var lines = tableText.Split('\n');

        // 캐럿이 어느 row·cell·셀 내 raw offset에 있는지 먼저 식별 — 그 셀은 정렬에서 제외 (raw 유지)
        var caretLoc = LocateCaretInTable(tableText, caret - tableStart);
        int activeRow = caretLoc.Row, activeCell = caretLoc.Cell;

        // 각 행을 cell list로 split (trim) + 원본 raw cell 보관 + separator 판정
        var rows = new List<string[]>();
        var rawCells = new List<string[]>();
        var isSep = new List<bool>();
        int colCount = 0;
        foreach (var ln in lines)
        {
            var trimmed = ln.TrimEnd('\r');
            var cells = SplitTableCells(trimmed);
            var raws = SplitTableCellsRaw(trimmed);
            if (cells.Length > colCount) colCount = cells.Length;
            rows.Add(cells);
            rawCells.Add(raws);
            isSep.Add(IsSeparatorOnly(trimmed));
        }
        if (colCount == 0) return;

        // 컬럼별 max content px width (separator 제외) — 한글이 영문 2배가 정확하지 않은
        // monospace 폰트(Consolas + 맑은 고딕 fallback 등)도 정확하게 정렬
        var tf = new Typeface(TextEditor.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double fontSize = TextEditor.FontSize;
        double spaceW = MeasurePx(" ", tf, fontSize);
        double dashW = MeasurePx("-", tf, fontSize);
        if (spaceW <= 0) spaceW = fontSize * 0.5;
        if (dashW <= 0) dashW = spaceW;

        var maxPx = new double[colCount];
        var contentPx = new double[rows.Count, colCount];
        for (int ri = 0; ri < rows.Count; ri++)
        {
            if (isSep[ri]) continue;
            for (int ci = 0; ci < rows[ri].Length && ci < colCount; ci++)
            {
                // active 셀은 raw 전체(좌우 공백 포함) 폭으로 max에 반영해 다른 셀이 그 폭에 맞춤
                double w;
                if (ri == activeRow && ci == activeCell)
                    w = MeasurePx(ci < rawCells[ri].Length ? rawCells[ri][ci] : string.Empty, tf, fontSize);
                else
                    w = MeasurePx(rows[ri][ci], tf, fontSize) + 2 * spaceW;  // 좌우 공백 포함 폭
                contentPx[ri, ci] = w;
                if (w > maxPx[ci]) maxPx[ci] = w;
            }
        }

        // 컬럼별 padding 공백 개수 (각 셀) + separator dash 개수 (컬럼당)
        // 셀 폭(`|` 사이): 좌측 공백 1 + content + padCount 공백 + 우측 공백 1
        //   목표 px = spaceW + maxPx[ci] + spaceW = maxPx[ci] + 2*spaceW
        //   현재 content px = contentPx[ri,ci]
        //   padCount = round((maxPx[ci] - contentPx[ri,ci]) / spaceW)
        // separator dashCount = round((maxPx[ci] + 2*spaceW) / dashW) — 셀 폭에 맞춤
        // dashCount: separator 셀의 `-` 개수가 다른 셀 폭(maxPx[ci])과 같은 px가 되도록
        var dashCount = new int[colCount];
        for (int ci = 0; ci < colCount; ci++)
        {
            int dc = (int)Math.Round(maxPx[ci] / dashW);
            if (dc < 3) dc = 3;
            dashCount[ci] = dc;
        }

        // 재조립
        var sb = new System.Text.StringBuilder();
        for (int ri = 0; ri < rows.Count; ri++)
        {
            sb.Append('|');
            for (int ci = 0; ci < colCount; ci++)
            {
                if (isSep[ri])
                {
                    sb.Append(new string('-', dashCount[ci]));
                }
                else if (ri == activeRow && ci == activeCell)
                {
                    // 사용자 편집 중인 셀 — raw 그대로 보존(좌우 공백 포함)
                    sb.Append(ci < rawCells[ri].Length ? rawCells[ri][ci] : string.Empty);
                }
                else
                {
                    var cell = ci < rows[ri].Length ? rows[ri][ci] : string.Empty;
                    // contentPx[ri,ci]는 이미 좌우 공백 포함된 셀 폭. 부족분만 공백 패딩 추가.
                    int pad = (int)Math.Round((maxPx[ci] - contentPx[ri, ci]) / spaceW);
                    if (pad < 0) pad = 0;
                    sb.Append(' ').Append(cell).Append(new string(' ', pad)).Append(' ');
                }
                sb.Append('|');
            }
            if (ri < rows.Count - 1) sb.Append('\n');
        }
        var newTableText = sb.ToString();
        if (newTableText == tableText) return;

        // 캐럿 보존: active 셀은 raw 보존되므로 raw offset 그대로, 다른 셀이면 content offset 기반
        int newCaret = caret;
        if (caret >= tableStart && caret <= tableEnd)
        {
            newCaret = tableStart + LocateCharInTable(newTableText, caretLoc.Row, caretLoc.Cell,
                caretLoc.RawOffset, caretLoc.ContentOffset, activeRow, activeCell);
        }
        else if (caret > tableEnd)
        {
            newCaret = caret + (newTableText.Length - tableText.Length);
        }

        _suppressAlign = true;
        try
        {
            var before = t.Substring(0, tableStart);
            var after = t.Substring(tableEnd);
            TextEditor.Text = before + newTableText + after;
            TextEditor.CaretIndex = Math.Clamp(newCaret, 0, TextEditor.Text.Length);
        }
        finally { _suppressAlign = false; }
    }

    private static double MeasurePx(string s, Typeface tf, double size)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var ft = new FormattedText(s,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, tf, size,
            System.Windows.Media.Brushes.Black, 1.0);
        return ft.Width;
    }

    // tableText 안에서 char offset → (row, cell, raw offset, content offset)
    // RawOffset: 셀 시작(`|` 다음) 기준 char offset (좌우 공백 포함)
    // ContentOffset: trimmed content 안에서의 offset (좌측 공백 제외)
    private static (int Row, int Cell, int RawOffset, int ContentOffset) LocateCaretInTable(string tableText, int offsetInTable)
    {
        int row = 0, cell = -1, lastPipe = -1;
        for (int i = 0; i < offsetInTable && i < tableText.Length; i++)
        {
            char ch = tableText[i];
            if (ch == '\n') { row++; cell = -1; lastPipe = -1; }
            else if (ch == '|') { cell++; lastPipe = i; }
        }
        if (cell < 0 || lastPipe < 0) return (row, 0, 0, 0);

        int cellStart = lastPipe + 1;
        int nextPipe = tableText.IndexOf('|', offsetInTable);
        int nextNl = tableText.IndexOf('\n', offsetInTable);
        int cellEnd;
        if (nextPipe < 0) cellEnd = nextNl < 0 ? tableText.Length : nextNl;
        else if (nextNl >= 0 && nextNl < nextPipe) cellEnd = nextNl;
        else cellEnd = nextPipe;

        int leftSp = 0;
        while (cellStart + leftSp < cellEnd && tableText[cellStart + leftSp] == ' ') leftSp++;
        int rightSp = 0;
        while (cellEnd - 1 - rightSp >= cellStart && tableText[cellEnd - 1 - rightSp] == ' ') rightSp++;
        int contentLen = Math.Max(0, cellEnd - cellStart - leftSp - rightSp);
        int raw = Math.Max(0, offsetInTable - cellStart);
        int trimmed = Math.Max(0, Math.Min(contentLen, raw - leftSp));
        return (row, cell, raw, trimmed);
    }

    // newTableText에서 (row, cell, rawOffset, contentOffset) + active 여부 → char index
    private static int LocateCharInTable(string newTableText, int targetRow, int targetCell,
        int targetRawOffset, int targetContentOffset, int activeRow, int activeCell)
    {
        int row = 0, cell = -1;
        for (int i = 0; i < newTableText.Length; i++)
        {
            char ch = newTableText[i];
            if (ch == '\n') { row++; cell = -1; }
            else if (ch == '|')
            {
                cell++;
                if (row == targetRow && cell == targetCell)
                {
                    int cellStart = i + 1;
                    int nextPipe = newTableText.IndexOf('|', cellStart);
                    int nextNl = newTableText.IndexOf('\n', cellStart);
                    int cellEnd;
                    if (nextPipe < 0) cellEnd = nextNl < 0 ? newTableText.Length : nextNl;
                    else if (nextNl >= 0 && nextNl < nextPipe) cellEnd = nextNl;
                    else cellEnd = nextPipe;

                    if (row == activeRow && cell == activeCell)
                    {
                        // active 셀은 raw 보존됐으므로 raw offset 그대로
                        return Math.Min(cellStart + targetRawOffset, cellEnd);
                    }
                    int leftSp = 0;
                    while (cellStart + leftSp < cellEnd && newTableText[cellStart + leftSp] == ' ') leftSp++;
                    return Math.Min(cellStart + leftSp + targetContentOffset, cellEnd);
                }
            }
        }
        return newTableText.Length;
    }

    // trim 안 한 raw cell split — active 셀 보존용
    private static string[] SplitTableCellsRaw(string row)
    {
        if (row.Length < 2 || row[0] != '|' || row[^1] != '|') return new[] { row };
        var inner = row.Substring(1, row.Length - 2);
        return inner.Split('|');
    }

    private static string[] SplitTableCells(string row)
    {
        if (row.Length < 2 || row[0] != '|' || row[^1] != '|') return new[] { row.Trim() };
        var inner = row.Substring(1, row.Length - 2);
        var parts = inner.Split('|');
        for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
        return parts;
    }

    private static bool IsTableRowLine(string text, int start, int endExclusive)
    {
        int e = endExclusive;
        if (e > start && text[e - 1] == '\r') e--;
        int len = e - start;
        if (len < 3) return false;
        if (text[start] != '|' || text[e - 1] != '|') return false;
        for (int i = start + 1; i < e - 1; i++)
            if (text[i] == '|') return true;
        return false;
    }

    private static bool IsSeparatorOnly(string line)
    {
        if (line.Length < 3 || line[0] != '|' || line[^1] != '|') return false;
        foreach (var ch in line) if (ch != '|' && ch != '-' && ch != ':' && ch != ' ') return false;
        return line.Contains('-');
    }

    // 한글/CJK 글자는 monospace에서도 영문의 약 2배 폭 — display width 2로 카운트
    private static int DisplayWidth(string s)
    {
        int w = 0;
        foreach (var ch in s) w += IsWideChar(ch) ? 2 : 1;
        return w;
    }

    private static bool IsWideChar(char ch)
    {
        int c = ch;
        return (c >= 0xAC00 && c <= 0xD7A3)   // 한글 음절
            || (c >= 0x1100 && c <= 0x11FF)   // 한글 자모
            || (c >= 0x3130 && c <= 0x318F)   // 한글 호환 자모
            || (c >= 0x4E00 && c <= 0x9FFF)   // CJK 통합
            || (c >= 0x3000 && c <= 0x303F)   // CJK 기호
            || (c >= 0xFF00 && c <= 0xFFEF)   // 전각
            || (c >= 0x30A0 && c <= 0x30FF)   // 가타카나
            || (c >= 0x3040 && c <= 0x309F);  // 히라가나
    }

    private void TextEditor_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateLineNumbers();
            _tableAdorner?.InvalidateVisual();
        }), DispatcherPriority.Loaded);
    }

    private ScrollViewer? _editorScrollViewer;
    private void HookEditorScroll()
    {
        if (_editorScrollViewer is not null) return;
        var sv = FindDescendant<ScrollViewer>(TextEditor);
        if (sv is null) return;
        _editorScrollViewer = sv;
        // 스크롤 시 줄 번호·표 그리드 위치도 다시 그림 (GetRectFromCharacterIndex는 스크롤된 visual 좌표 반환)
        sv.ScrollChanged += (_, _) =>
        {
            UpdateLineNumbers();
            _tableAdorner?.InvalidateVisual();
        };
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (CurrentSettings is { } svc)
        {
            svc.SettingsChanged -= OnSettingsChanged;  // 중복 구독 방지
            svc.SettingsChanged += OnSettingsChanged;
        }
        App.MainVM.QueryResultsInvalidated -= OnQueryInvalidated;
        App.MainVM.QueryResultsInvalidated += OnQueryInvalidated;
        ApplyEditorFont();
        // RefreshDocument는 Markdig 파싱 + 임베드 이미지 디코드를 동기로 수행해 노트북을
        // 처음 열 때 노트 N개의 동시 비용이 UI 스레드를 블로킹. Background priority로
        // 미뤄 첫 표시는 빈 FlowDocument로 즉시, 마크다운은 사용자가 보는 동안 채워지게 함.
        // 단 자동 편집 진입(아래)은 즉시 실행 — Loaded 시점 IsEditing 플래그 처리.
        Dispatcher.BeginInvoke(new Action(RefreshDocument), DispatcherPriority.Background);

        // 새로 생성된 노트는 IsEditing=true로 만들어져서 자동으로 편집 모드로 진입
        if (Item is { IsEditing: true, Type: NoteType.Text })
        {
            Item.IsEditing = false;
            BeginEdit();
        }
    }

    private void UserControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Item is null) return;

        // 다른 노트의 TextBox에서 편집 중이라면 LostFocus를 트리거해 표시 모드로 복귀시킴.
        if (!IsInsideTextBox(e.OriginalSource) && Window.GetWindow(this) is MainWindow mw)
            mw.FocusCanvas();

        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (shift)
        {
            // Shift+클릭: 토글, 드래그/편집 차단
            Item.IsSelected = !Item.IsSelected;
            e.Handled = true;
            return;
        }
        // 미선택 상태에서 일반 클릭: 다른 선택 모두 해제 후 이 노트만 선택
        // 이미 선택된 경우는 그대로 두고 그룹 드래그가 진행되도록 함
        if (!Item.IsSelected)
            App.MainVM.SelectOnly(Item);

        // 본문 드래그 후보 setup — 단일 클릭 + 편집 중 아님 + BodyArea 자손이면 활성.
        // 4px 임계값으로 단순 클릭(선택만)과 클릭+드래그를 구분 → 미선택 상태 클릭+드래그도
        // 이 클릭에서 즉시 선택+드래그로 자연스럽게 이어짐.
        if (e.ClickCount > 1) return;
        if (Item.Type == NoteType.Text && EditorContainer.Visibility == Visibility.Visible) return;
        if (!IsOnBodyArea(e.OriginalSource)) return;

        var canvas = FindParentCanvas();
        if (canvas is null) return;
        _bodyPotentialDrag = true;
        _bodyDragStartScreen = e.GetPosition(canvas);
    }

    private void UserControl_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_bodyPotentialDrag)
        {
            var canvas = FindParentCanvas();
            if (canvas is null) return;
            var pos = e.GetPosition(canvas);
            if (Math.Abs(pos.X - _bodyDragStartScreen.X) < 4 && Math.Abs(pos.Y - _bodyDragStartScreen.Y) < 4) return;

            // 임계값 초과 — 드래그로 승격
            _bodyPotentialDrag = false;
            _isDragging = true;
            _dragStart = _bodyDragStartScreen;
            BuildDragSnapshots();
            // UserControl 자신이 캡처를 잡음 — 후속 MouseMove/Up이 모두 이 컨트롤로 라우팅
            ((UIElement)sender).CaptureMouse();
            // 드래그 중 커서를 SizeAll로 강제 — UserControl.Cursor가 기본값이라 캡처 중에는
            // BodyArea의 DataTrigger Cursor가 적용 안 됨. OverrideCursor로 전역 강제.
            Mouse.OverrideCursor = Cursors.SizeAll;
            e.Handled = true;
        }
        // UC가 캡처를 가진 경우에만(=본문 드래그) move 처리. 타이틀바 드래그는 타이틀바 Border가
        // 캡처를 잡고 자체 MouseMove로 처리하므로 여기서 중복 호출/간섭하지 않음.
        if (_isDragging && IsMouseCaptured) TitleBar_MouseMove(sender, e);
    }

    private void UserControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_bodyPotentialDrag)
        {
            // 단순 클릭(이동 < 임계값)으로 끝 — 드래그 안 됨, 자식 이벤트(예: 더블클릭) 정상 흐름
            _bodyPotentialDrag = false;
            return;
        }
        // UC가 캡처를 가진 경우에만(=본문 드래그) cleanup. 타이틀바 드래그는 타이틀바 Border가
        // 자체 MouseLeftButtonUp에서 cleanup하므로 가로채면 안 됨 — 가로채면 _isDragging이 먼저
        // false로 리셋돼 타이틀바 핸들러가 ReleaseMouseCapture를 못 호출하고 캡처가 leak됨.
        if (_isDragging && IsMouseCaptured)
        {
            TitleBar_MouseLeftButtonUp(sender, e);
            Mouse.OverrideCursor = null;
            e.Handled = true;
        }
    }

    private void TextEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        DetachTableAdorner();
        EditorContainer.Visibility = Visibility.Collapsed;
        TextDisplayScroll.Visibility = Visibility.Visible;
        if (Item is not null)
            App.MainVM.UpdateNoteContent(Item);
        RefreshDocument();  // 편집 중엔 갱신을 미뤘으므로 표시 복귀 시점에 한 번 갱신
    }

    private void TextEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // ESC로 편집 종료. CanvasArea(Focusable=True)에 포커스를 옮겨 LostFocus 트리거 +
            // 이후 화살표가 Window_PreviewKeyDown에 도달하도록 함
            if (Window.GetWindow(this) is MainWindow mw) mw.FocusCanvas();
            e.Handled = true;
            return;
        }

        // Tab / Shift+Tab: 표 안이면 다음/이전 셀로 점프, 그 외엔 들여쓰기 / 내어쓰기.
        if (e.Key == Key.Tab)
        {
            bool back = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            if (TryHandleTableTab(back))
            {
                e.Handled = true;
                return;
            }
            HandleIndent(back);
            e.Handled = true;
            return;
        }

        // Enter: 표 행 끝에서 누르면 새 행(헤더 다음이면 separator 자동 보강)
        if (e.Key == Key.Return && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (TryHandleTableEnter())
            {
                e.Handled = true;
                return;
            }
        }

        // Ctrl+T: 빈 표 삽입 다이얼로그
        if (e.Key == Key.T && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            InsertTableTemplate();
            e.Handled = true;
            return;
        }

        // Ctrl+V: 클립보드에 이미지가 있으면 파일 저장 + 마크다운 ![](파일명) 캐럿 위치에 삽입.
        // 이미지가 없으면 e.Handled=false로 양보 → TextBox 기본 텍스트 붙여넣기 동작
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            var savedPath = App.MainVM.SaveClipboardImageIfPresent();
            if (savedPath is null) return;

            var fileName = System.IO.Path.GetFileName(savedPath);
            var insert = $"![]({fileName})";

            var caret = TextEditor.CaretIndex;
            var text = TextEditor.Text ?? string.Empty;
            var prefix = (caret > 0 && text[caret - 1] != '\n') ? "\n" : "";
            var suffix = (caret < text.Length && text[caret] != '\n') ? "\n" : "";
            var combined = prefix + insert + suffix;

            TextEditor.Text = text.Insert(caret, combined);
            TextEditor.CaretIndex = caret + combined.Length;
            e.Handled = true;
        }
        // Alt+방향키 노트 이동은 Window_PreviewKeyDown에서 통합 처리
    }

    private const string IndentUnit = "    ";

    // Tab / Shift+Tab 들여쓰기 처리.
    // - 캐럿만 있고 indent: 캐럿 위치에 4칸 삽입
    // - 단일 줄 selection + indent: selection 자리를 4칸으로 치환
    // - 그 외(다중 줄 selection / outdent): 영향받는 줄 머리에서 일괄 추가/제거
    //   outdent는 줄 시작의 공백을 최대 4칸까지, 또는 선두 탭 1개 제거
    private void HandleIndent(bool outdent)
    {
        var text = TextEditor.Text ?? string.Empty;
        int selStart = TextEditor.SelectionStart;
        int selLen = TextEditor.SelectionLength;
        int selEnd = selStart + selLen;
        bool multiLine = selLen > 0 && text.AsSpan(selStart, selLen).IndexOf('\n') >= 0;

        if (!outdent && selLen == 0)
        {
            TextEditor.Text = text.Insert(selStart, IndentUnit);
            TextEditor.CaretIndex = selStart + IndentUnit.Length;
            return;
        }
        if (!outdent && !multiLine)
        {
            TextEditor.Text = text.Substring(0, selStart) + IndentUnit + text.Substring(selEnd);
            TextEditor.CaretIndex = selStart + IndentUnit.Length;
            return;
        }

        // 줄 단위 모드: 영향 영역을 줄 경계로 확장
        int regionStart = LineStart(text, selStart);
        // selection이 줄 시작 지점에서 끝나면(직전이 \n) 그 빈 줄은 포함하지 않음
        int probeEnd = selLen == 0
            ? selStart
            : (selEnd > selStart && text[selEnd - 1] == '\n' ? selEnd - 1 : selEnd);
        int regionEnd = LineEnd(text, probeEnd);

        string before = text.Substring(0, regionStart);
        string region = text.Substring(regionStart, regionEnd - regionStart);
        string after = text.Substring(regionEnd);

        var lines = region.Split('\n');
        int firstLineDelta = 0;
        int totalDelta = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            int delta;
            if (outdent)
            {
                int remove = 0;
                if (line.Length > 0 && line[0] == '\t')
                {
                    remove = 1;
                }
                else
                {
                    for (int j = 0; j < IndentUnit.Length && j < line.Length && line[j] == ' '; j++)
                        remove++;
                }
                lines[i] = line.Substring(remove);
                delta = -remove;
            }
            else
            {
                lines[i] = IndentUnit + line;
                delta = IndentUnit.Length;
            }
            if (i == 0) firstLineDelta = delta;
            totalDelta += delta;
        }

        TextEditor.Text = before + string.Join('\n', lines) + after;

        if (selLen == 0)
        {
            TextEditor.CaretIndex = Math.Max(regionStart, selStart + firstLineDelta);
        }
        else
        {
            int newStart = Math.Max(regionStart, selStart + firstLineDelta);
            int newEnd = Math.Max(newStart, selEnd + totalDelta);
            TextEditor.Select(newStart, newEnd - newStart);
        }
    }

    // 표 행 판정: 줄이 '|'로 시작/끝나고 중간에 '|'가 1개 이상
    private static bool IsTableRowLine(string line) =>
        line.Length >= 3 && line[0] == '|' && line[^1] == '|' && line.IndexOf('|', 1) < line.Length - 1;

    // separator 줄 판정: '|', '-', ':', 공백만으로 구성된 표 행 (열 정렬 메타)
    private static bool IsSeparatorLine(string line)
    {
        if (!IsTableRowLine(line)) return false;
        foreach (var ch in line) if (ch != '|' && ch != '-' && ch != ':' && ch != ' ') return false;
        return line.Contains('-');
    }

    private static int CountColumns(string line)
    {
        if (!IsTableRowLine(line)) return 0;
        int pipes = 0;
        foreach (var ch in line) if (ch == '|') pipes++;
        return pipes - 1;
    }

    private bool TryHandleTableTab(bool back)
    {
        var text = TextEditor.Text ?? string.Empty;
        var caret = TextEditor.CaretIndex;
        int ls = LineStart(text, caret);
        int le = LineEnd(text, caret);
        var line = text.Substring(ls, le - ls);
        if (!IsTableRowLine(line)) return false;
        if (IsSeparatorLine(line))
        {
            // separator 줄은 사용자가 직접 편집할 일이 거의 없음 — 인접 행 첫/마지막 셀로 점프
            return JumpToAdjacentRowCell(text, ls, le, back, firstCellOfNext: !back);
        }

        if (back)
        {
            // 캐럿 좌측의 가장 가까운 '|'를 찾되, 자신이 시작 '|'면 이전 행으로
            int leftPipe = text.LastIndexOf('|', Math.Max(ls, caret - 1));
            if (leftPipe <= ls)
                return JumpToAdjacentRowCell(text, ls, le, back: true, firstCellOfNext: false);
            // 그 좌측 셀의 시작 = 그 이전 '|' 다음 위치
            int prevPipe = text.LastIndexOf('|', leftPipe - 1);
            if (prevPipe < ls) return false;
            int newCaret = SkipOneSpace(text, prevPipe + 1, leftPipe);
            TextEditor.CaretIndex = newCaret;
            return true;
        }
        else
        {
            // 캐럿 우측의 가장 가까운 '|'를 찾고 그 다음 셀 시작으로
            int rightPipe = text.IndexOf('|', caret);
            if (rightPipe < 0 || rightPipe >= le) return false;
            if (rightPipe == le - 1)
            {
                // 줄 끝 '|' — 다음 행 첫 셀로 이동 (없으면 새 행 자동 추가)
                return JumpToAdjacentRowCell(text, ls, le, back: false, firstCellOfNext: true);
            }
            int next = rightPipe + 1;
            int nextEnd = text.IndexOf('|', next);
            if (nextEnd < 0 || nextEnd > le) nextEnd = le;
            int newCaret = SkipOneSpace(text, next, nextEnd);
            TextEditor.CaretIndex = newCaret;
            return true;
        }
    }

    private static int SkipOneSpace(string text, int from, int hardLimit)
    {
        if (from < hardLimit && from < text.Length && text[from] == ' ') return from + 1;
        return from;
    }

    private bool JumpToAdjacentRowCell(string text, int curLs, int curLe, bool back, bool firstCellOfNext)
    {
        if (back)
        {
            if (curLs == 0) return true;  // 첫 줄 — 무동작
            int prevLe = curLs - 1;
            int prevLs = LineStart(text, prevLe - 1);
            var prev = text.Substring(prevLs, prevLe - prevLs);
            if (IsSeparatorLine(prev))
            {
                if (prevLs == 0) return true;
                int p2Le = prevLs - 1;
                int p2Ls = LineStart(text, p2Le - 1);
                prev = text.Substring(p2Ls, p2Le - p2Ls);
                prevLs = p2Ls; prevLe = p2Le;
            }
            if (!IsTableRowLine(prev)) return true;
            // 이전 행 마지막 셀: 마지막 '|' 직전 셀
            int lastPipe = prev.LastIndexOf('|');
            int secondLastPipe = prev.LastIndexOf('|', lastPipe - 1);
            if (secondLastPipe < 0) return true;
            int caretLocal = secondLastPipe + 1;
            if (caretLocal < lastPipe && prev[caretLocal] == ' ') caretLocal++;
            TextEditor.CaretIndex = prevLs + caretLocal;
            return true;
        }
        else
        {
            // 다음 줄 검사 — separator면 건너뜀
            int nextLs = curLe < text.Length && text[curLe] == '\n' ? curLe + 1 : curLe;
            if (nextLs >= text.Length)
            {
                // 새 행 자동 추가 + 캐럿
                AppendNewTableRow(text, curLs, curLe);
                return true;
            }
            int nextLe = LineEnd(text, nextLs);
            var nextLine = text.Substring(nextLs, nextLe - nextLs);
            if (IsSeparatorLine(nextLine))
            {
                // 헤더 다음 separator를 건너뛰고 그 다음 줄 확인
                int n2Ls = nextLe < text.Length && text[nextLe] == '\n' ? nextLe + 1 : nextLe;
                if (n2Ls >= text.Length)
                {
                    AppendNewTableRow(text, curLs, curLe);
                    return true;
                }
                int n2Le = LineEnd(text, n2Ls);
                var n2Line = text.Substring(n2Ls, n2Le - n2Ls);
                if (!IsTableRowLine(n2Line))
                {
                    AppendNewTableRow(text, nextLe, nextLe);
                    return true;
                }
                nextLs = n2Ls; nextLe = n2Le; nextLine = n2Line;
            }
            else if (!IsTableRowLine(nextLine))
            {
                AppendNewTableRow(text, curLs, curLe);
                return true;
            }
            // 다음 행 첫 셀: 첫 '|' 다음 위치
            int firstPipe = nextLine.IndexOf('|');
            int caretLocal = firstPipe + 1;
            int secondPipe = nextLine.IndexOf('|', caretLocal);
            if (secondPipe < 0) secondPipe = nextLine.Length;
            if (caretLocal < secondPipe && nextLine[caretLocal] == ' ') caretLocal++;
            TextEditor.CaretIndex = nextLs + caretLocal;
            return true;
        }
    }

    // 표 행 다음에 새 빈 행을 삽입 + 캐럿을 첫 셀로
    private void AppendNewTableRow(string text, int referenceLs, int referenceLe)
    {
        var refLine = text.Substring(referenceLs, referenceLe - referenceLs);
        int cols = CountColumns(refLine);
        if (cols <= 0) return;
        var sb = new System.Text.StringBuilder("\n|");
        for (int i = 0; i < cols; i++) sb.Append("   |");
        var insert = sb.ToString();
        TextEditor.Text = text.Insert(referenceLe, insert);
        // 새 행 첫 셀 — referenceLe + "\n|".Length + (space 1)
        int firstCell = referenceLe + 2 + 1;
        TextEditor.CaretIndex = firstCell;
    }

    private bool TryHandleTableEnter()
    {
        var text = TextEditor.Text ?? string.Empty;
        var caret = TextEditor.CaretIndex;
        int ls = LineStart(text, caret);
        int le = LineEnd(text, caret);
        var line = text.Substring(ls, le - ls);
        if (!IsTableRowLine(line)) return false;
        if (IsSeparatorLine(line)) return false;
        // 줄 끝이 아니면 양보 (셀 안 위치라면 사용자가 줄바꿈 의도)
        if (caret != le) return false;

        int cols = CountColumns(line);
        if (cols <= 0) return false;

        // 헤더 판정: 위쪽에 표 행이 없으면 현재 줄이 헤더.
        bool isHeader = IsHeaderRow(text, ls);
        // 바로 아래 줄이 이미 separator인지
        bool separatorBelow = false;
        if (le < text.Length && text[le] == '\n')
        {
            int nls = le + 1;
            int nle = LineEnd(text, nls);
            var nline = text.Substring(nls, nle - nls);
            separatorBelow = IsSeparatorLine(nline);
        }
        bool needSeparator = isHeader && !separatorBelow;

        // 헤더이고 separator가 이미 아래 있으면 separator 다음에 새 데이터 행 삽입 (그러지 않으면 표가 깨짐)
        int insertAt = le;
        if (isHeader && separatorBelow && le < text.Length && text[le] == '\n')
        {
            int nls = le + 1;
            int nle = LineEnd(text, nls);
            insertAt = nle;
        }

        var sb = new System.Text.StringBuilder();
        if (needSeparator)
        {
            sb.Append("\n|");
            for (int i = 0; i < cols; i++) sb.Append("---|");
        }
        sb.Append("\n|");
        for (int i = 0; i < cols; i++) sb.Append("   |");
        var insert = sb.ToString();
        TextEditor.Text = text.Insert(insertAt, insert);
        // insert 안에서 마지막 "\n|"가 새 데이터 행 시작
        int lastNewLine = insert.LastIndexOf('\n');
        int caretAt = insertAt + lastNewLine + 2;  // '\n|' 다음
        if (caretAt < TextEditor.Text.Length && TextEditor.Text[caretAt] == ' ') caretAt++;
        TextEditor.CaretIndex = caretAt;
        return true;
    }

    // 현재 표 행이 헤더인가? 위쪽에 또 다른 표 행이 있으면 헤더 아님.
    private static bool IsHeaderRow(string text, int rowLs)
    {
        if (rowLs == 0) return true;
        if (text[rowLs - 1] != '\n') return true;
        int prevEnd = rowLs - 1;
        int prevStart = LineStart(text, prevEnd > 0 ? prevEnd - 1 : 0);
        var prev = text.Substring(prevStart, prevEnd - prevStart);
        return !IsTableRowLine(prev);
    }

    private void InsertTableTemplate()
    {
        var dlg = new Dialogs.InsertTableDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        var md = dlg.BuildMarkdown();

        var text = TextEditor.Text ?? string.Empty;
        var caret = TextEditor.CaretIndex;
        // 캐럿이 줄 중간이면 위/아래에 줄바꿈 prefix/suffix 추가
        var prefix = (caret > 0 && text[caret - 1] != '\n') ? "\n" : "";
        var suffix = (caret < text.Length && text[caret] != '\n') ? "\n" : "";
        var combined = prefix + md + suffix;

        TextEditor.Text = text.Insert(caret, combined);
        // 캐럿을 헤더 첫 셀로
        int firstCell = caret + prefix.Length + 1; // '|' 다음
        if (firstCell < TextEditor.Text.Length && TextEditor.Text[firstCell] == ' ') firstCell++;
        TextEditor.CaretIndex = firstCell;
    }

    private static int LineStart(string text, int pos)
    {
        if (pos <= 0) return 0;
        int idx = text.LastIndexOf('\n', Math.Min(pos - 1, text.Length - 1));
        return idx == -1 ? 0 : idx + 1;
    }

    private static int LineEnd(string text, int pos)
    {
        if (pos >= text.Length) return text.Length;
        int idx = text.IndexOf('\n', pos);
        return idx == -1 ? text.Length : idx;
    }

    // 리사이즈 시작 시점의 마우스 절대 위치(부모 캔버스 좌표계). thumb 자체가 리사이즈로
    // 같이 움직일 때 thumb-local 기준 누적 델타가 양자화된 스냅과 만나 진동(점프 백)을
    // 유발하므로, 캔버스 기준의 안정적인 좌표를 직접 비교한다.
    private Point _resizeMouseStart;

    private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (Item is null) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;
        _resizeEdge = (sender as FrameworkElement)?.Tag as string ?? "BottomRight";
        var group = App.MainVM.SelectedNotes.ToList();
        if (!group.Contains(Item)) group = new List<NoteItem> { Item };
        _resizeGroup = group.Select(n => (n, n.X, n.Y, n.Width, n.Height)).ToList();
        _resizeMouseStart = Mouse.GetPosition(canvas);
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_resizeGroup.Count == 0) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;
        var mouseNow = Mouse.GetPosition(canvas);
        var dx = mouseNow.X - _resizeMouseStart.X;
        var dy = mouseNow.Y - _resizeMouseStart.Y;
        const double minW = 80, minH = 40;
        foreach (var (it, sx, sy, sw, sh) in _resizeGroup)
        {
            // 좌/상은 X/Y와 W/H 동시 변경(반대편 엣지 고정), 우/하는 W/H만
            bool left = _resizeEdge is "Left" or "TopLeft" or "BottomLeft";
            bool right = _resizeEdge is "Right" or "TopRight" or "BottomRight";
            bool top = _resizeEdge is "Top" or "TopLeft" or "TopRight";
            bool bottom = _resizeEdge is "Bottom" or "BottomLeft" or "BottomRight";

            if (right)
            {
                it.Width = Math.Max(minW, App.MainVM.MaybeSnap(sw + dx));
            }
            if (left)
            {
                var rightEdge = sx + sw;
                var newX = App.MainVM.MaybeSnap(sx + dx);
                if (rightEdge - newX < minW) newX = rightEdge - minW;
                it.X = newX;
                it.Width = rightEdge - newX;
            }
            if (bottom)
            {
                it.Height = Math.Max(minH, App.MainVM.MaybeSnap(sh + dy));
            }
            if (top)
            {
                var bottomEdge = sy + sh;
                var newY = App.MainVM.MaybeSnap(sy + dy);
                if (bottomEdge - newY < minH) newY = bottomEdge - minH;
                it.Y = newY;
                it.Height = bottomEdge - newY;
            }
        }
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        var changes = _resizeGroup
            .Where(t => t.Item.X != t.StartX || t.Item.Y != t.StartY
                     || t.Item.Width != t.StartW || t.Item.Height != t.StartH)
            .Select(t => (
                Old: new Services.ItemSnapshot(t.Item, t.StartX, t.StartY, t.StartW, t.StartH),
                New: new Services.ItemSnapshot(t.Item, t.Item.X, t.Item.Y, t.Item.Width, t.Item.Height)))
            .ToList();
        foreach (var (it, _, _, _, _) in _resizeGroup)
            App.MainVM.UpdateNoteContent(it);
        if (changes.Count > 0) App.MainVM.RecordTransform(changes);
        _resizeGroup.Clear();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
            // 잘못된 URL은 조용히 무시
        }
        e.Handled = true;
    }
}
