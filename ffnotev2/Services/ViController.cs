using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ffnotev2.Services;

/// <summary>
/// TextBox 한 개에 대한 vi-like 상태 머신.
/// - Normal / Insert / Visual(char) / VisualLine / Command 모드
/// - 자체 undo 스택 (TextBox.IsUndoEnabled=false 우회)
/// - h/j/k/l, 0/$, gg/G, w/b, i/a/I/A/o/O, x/dd/yy/p, u/Ctrl+r, v/V, :명령
/// - 한글 IME: Key.ImeProcessed 키는 무시
/// 호스트는 `OnPreviewKeyDown`을 PreviewKeyDown에서 호출하고 `Handled` 반환을 보고 가로채야 함.
/// </summary>
public class ViController
{
    public enum Mode { Insert, Normal, VisualChar, VisualLine, Command }

    private readonly TextBox _editor;
    private readonly DispatcherTimer _pendingTimer;
    private char? _pending;
    private int _visualAnchor;
    private readonly LinkedList<TextSnapshot> _undo = new();
    private LinkedListNode<TextSnapshot>? _undoPointer;
    private const int UndoMax = 200;
    private string _lastSavedText;
    private readonly StringBuilder _commandBuffer = new();

    public Mode CurrentMode { get; private set; } = Mode.Insert;

    /// <summary>Command 모드에서 사용자가 입력 중인 ':' 명령 (호스트가 상태 바에 표시).</summary>
    public string CommandText => _commandBuffer.ToString();

    /// <summary>모드 전환·command 갱신 시 호출 (호스트가 시각 표시 갱신).</summary>
    public event Action? StateChanged;

    /// <summary>Command 모드에서 ':q' 등으로 편집 종료를 요청.</summary>
    public event Action? QuitRequested;

    public ViController(TextBox editor)
    {
        _editor = editor;
        _lastSavedText = editor.Text ?? string.Empty;
        _pendingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _pendingTimer.Tick += (_, _) => { _pending = null; _pendingTimer.Stop(); };
    }

    public void Reset(Mode initial)
    {
        CurrentMode = initial;
        _pending = null;
        _pendingTimer.Stop();
        _commandBuffer.Clear();
        _undo.Clear();
        _undoPointer = null;
        _lastSavedText = _editor.Text ?? string.Empty;
        StateChanged?.Invoke();
    }

    public void SetMode(Mode m)
    {
        if (CurrentMode == m) return;
        // Visual을 떠날 때 selection 정리
        if ((CurrentMode == Mode.VisualChar || CurrentMode == Mode.VisualLine) &&
            (m == Mode.Normal || m == Mode.Insert))
        {
            _editor.SelectionLength = 0;
        }
        if (m == Mode.Command) _commandBuffer.Clear();
        CurrentMode = m;
        StateChanged?.Invoke();
    }

    // 외부에서 임의 시점 스냅샷 push (예: insert→normal 전환 시)
    public void PushUndoSnapshot()
    {
        var text = _editor.Text ?? string.Empty;
        if (text == _lastSavedText) return;
        var snap = new TextSnapshot(_lastSavedText, _editor.CaretIndex);
        // pointer 이후 redo 가지 잘라내기
        while (_undoPointer is not null && _undoPointer.Next is not null)
            _undo.Remove(_undoPointer.Next);
        _undo.AddLast(snap);
        while (_undo.Count > UndoMax) _undo.RemoveFirst();
        _undoPointer = _undo.Last;
        _lastSavedText = text;
    }

    private void Undo()
    {
        if (_undoPointer is null) return;
        var prev = _undoPointer.Value;
        // 현재 상태도 redo용으로 보존
        var current = new TextSnapshot(_editor.Text ?? string.Empty, _editor.CaretIndex);
        _editor.Text = prev.Text;
        _editor.CaretIndex = Math.Clamp(prev.Caret, 0, _editor.Text.Length);
        _lastSavedText = _editor.Text;
        // 포인터를 한 단계 뒤로. 끝에 도달했으면 null (더 이상 undo 불가)
        if (_undoPointer.Previous is not null)
        {
            _undoPointer = _undoPointer.Previous;
        }
        else
        {
            // 끝까지 — 다음 undo는 없지만 redo 위해 현재(원래) 상태를 last로 보존
            // 별도 redo 스택 없이 LinkedList로 양방향 처리하기 위해 노드 추가
            _undo.AddLast(current);
            _undoPointer = _undo.Last?.Previous;
        }
    }

    private void Redo()
    {
        if (_undoPointer is null || _undoPointer.Next is null) return;
        var next = _undoPointer.Next.Value;
        _editor.Text = next.Text;
        _editor.CaretIndex = Math.Clamp(next.Caret, 0, _editor.Text.Length);
        _lastSavedText = _editor.Text;
        _undoPointer = _undoPointer.Next;
    }

    public bool OnPreviewKeyDown(KeyEventArgs e)
    {
        // IME 합성 키는 절대 가로채지 않음
        if (e.Key == Key.ImeProcessed || e.Key == Key.DeadCharProcessed)
            return false;

        // Insert 모드: Esc만 가로챔
        if (CurrentMode == Mode.Insert)
        {
            if (e.Key == Key.Escape)
            {
                PushUndoSnapshot();
                SetMode(Mode.Normal);
                return true;
            }
            return false;
        }

        if (CurrentMode == Mode.Command)
        {
            return HandleCommandKey(e);
        }

        // Normal / Visual
        return HandleNormalOrVisualKey(e);
    }

    /// <summary>호스트의 TextInput 이벤트에서 호출. Command 모드일 때 ':' 버퍼에 글자 누적.</summary>
    public bool OnTextInput(TextCompositionEventArgs e)
    {
        if (CurrentMode != Mode.Command) return false;
        if (string.IsNullOrEmpty(e.Text)) return true;
        foreach (var ch in e.Text)
        {
            if (ch == '\b' || ch == '\r' || ch == '\n' || ch == 27) continue;
            _commandBuffer.Append(ch);
        }
        StateChanged?.Invoke();
        return true;
    }

    private bool HandleCommandKey(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SetMode(Mode.Normal);
            return true;
        }
        if (e.Key == Key.Back)
        {
            if (_commandBuffer.Length > 0)
            {
                _commandBuffer.Length--;
                StateChanged?.Invoke();
            }
            else SetMode(Mode.Normal);
            return true;
        }
        if (e.Key == Key.Return || e.Key == Key.Enter)
        {
            ExecuteCommand(_commandBuffer.ToString());
            return true;
        }
        return false;  // 글자 입력은 OnTextInput에서 누적
    }

    private void ExecuteCommand(string cmd)
    {
        cmd = cmd.Trim();
        switch (cmd)
        {
            case "q":
            case "q!":
            case "wq":
            case "x":
                SetMode(Mode.Normal);
                _commandBuffer.Clear();
                QuitRequested?.Invoke();
                return;
            case "w":
                // 자동저장이라 no-op
                SetMode(Mode.Normal);
                _commandBuffer.Clear();
                StateChanged?.Invoke();
                return;
        }
        // 모르는 명령은 그냥 normal 복귀
        SetMode(Mode.Normal);
        _commandBuffer.Clear();
        StateChanged?.Invoke();
    }

    private bool HandleNormalOrVisualKey(KeyEventArgs e)
    {
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool isVisual = CurrentMode is Mode.VisualChar or Mode.VisualLine;

        // 모디파이어 단독은 무시 (pending도 유지)
        if (e.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return false;

        // Esc: Visual → Normal, Normal에선 selection 해제·pending 취소
        if (e.Key == Key.Escape)
        {
            if (isVisual)
            {
                SetMode(Mode.Normal);
                return true;
            }
            _editor.SelectionLength = 0;
            _pending = null;
            _pendingTimer.Stop();
            StateChanged?.Invoke();
            return true;
        }

        // 화살표는 Normal에서도 친절하게 동작하도록 허용 (vi 순수성 < 사용자 편의)
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            if (isVisual)
            {
                MoveCaret(e.Key);
                ExtendVisualSelection();
                return true;
            }
            return false;  // Normal에선 기본 TextBox 동작 양보(IsUndoEnabled=false라 안전)
        }

        // Ctrl+r → redo
        if (ctrl && e.Key == Key.R)
        {
            Redo();
            return true;
        }

        // 화면키 → 문자 매핑
        char? ch = KeyToVisible(e.Key, shift);
        if (ch is null) return false;
        char c = ch.Value;

        // pending 키 (g, d, y)는 단일 입력 시 무동작
        if (_pending == 'g')
        {
            _pending = null; _pendingTimer.Stop();
            if (c == 'g')
            {
                _editor.CaretIndex = 0;
                if (isVisual) ExtendVisualSelection();
                return true;
            }
        }
        else if (_pending == 'd')
        {
            _pending = null; _pendingTimer.Stop();
            if (c == 'd')
            {
                DeleteLine();
                return true;
            }
        }
        else if (_pending == 'y')
        {
            _pending = null; _pendingTimer.Stop();
            if (c == 'y')
            {
                YankLine();
                return true;
            }
        }

        // Visual 모드에서 d / y / x → 선택 영역 처리
        if (isVisual)
        {
            switch (c)
            {
                case 'h': case 'l': case 'j': case 'k': case '0': case '$':
                case 'w': case 'b':
                    MoveByVisualKey(c);
                    ExtendVisualSelection();
                    return true;
                case 'G':
                    _editor.CaretIndex = (_editor.Text ?? string.Empty).Length;
                    ExtendVisualSelection();
                    return true;
                case 'g':
                    _pending = 'g'; _pendingTimer.Stop(); _pendingTimer.Start();
                    return true;
                case 'd': case 'x':
                    DeleteSelection();
                    SetMode(Mode.Normal);
                    return true;
                case 'y':
                    YankSelection();
                    SetMode(Mode.Normal);
                    return true;
                case 'v':
                    SetMode(CurrentMode == Mode.VisualChar ? Mode.Normal : Mode.VisualChar);
                    return true;
                case 'V':
                    SetMode(CurrentMode == Mode.VisualLine ? Mode.Normal : Mode.VisualLine);
                    if (CurrentMode == Mode.VisualLine) ExtendVisualSelection();
                    return true;
                case ':':
                    SetMode(Mode.Command);
                    return true;
            }
            return false;
        }

        // Normal 모드
        switch (c)
        {
            case 'i': SetMode(Mode.Insert); return true;
            case 'I':
                _editor.CaretIndex = LineStart(_editor.Text ?? string.Empty, _editor.CaretIndex);
                SetMode(Mode.Insert);
                return true;
            case 'a':
                if (_editor.CaretIndex < (_editor.Text?.Length ?? 0)) _editor.CaretIndex++;
                SetMode(Mode.Insert);
                return true;
            case 'A':
                _editor.CaretIndex = LineEnd(_editor.Text ?? string.Empty, _editor.CaretIndex);
                SetMode(Mode.Insert);
                return true;
            case 'o':
                {
                    PushUndoSnapshot();
                    var t = _editor.Text ?? string.Empty;
                    int le = LineEnd(t, _editor.CaretIndex);
                    _editor.Text = t.Insert(le, "\n");
                    _editor.CaretIndex = le + 1;
                    SetMode(Mode.Insert);
                    return true;
                }
            case 'O':
                {
                    PushUndoSnapshot();
                    var t = _editor.Text ?? string.Empty;
                    int ls = LineStart(t, _editor.CaretIndex);
                    _editor.Text = t.Insert(ls, "\n");
                    _editor.CaretIndex = ls;
                    SetMode(Mode.Insert);
                    return true;
                }
            case 'h': case 'l': case 'j': case 'k': case '0': case '$':
            case 'w': case 'b':
                MoveByVisualKey(c);
                return true;
            case 'G':
                _editor.CaretIndex = (_editor.Text ?? string.Empty).Length;
                return true;
            case 'g':
                _pending = 'g'; _pendingTimer.Stop(); _pendingTimer.Start();
                return true;
            case 'd':
                _pending = 'd'; _pendingTimer.Stop(); _pendingTimer.Start();
                return true;
            case 'y':
                _pending = 'y'; _pendingTimer.Stop(); _pendingTimer.Start();
                return true;
            case 'x':
                {
                    PushUndoSnapshot();
                    var t = _editor.Text ?? string.Empty;
                    int caret = _editor.CaretIndex;
                    if (caret < t.Length && t[caret] != '\n')
                    {
                        _editor.Text = t.Remove(caret, 1);
                        _editor.CaretIndex = Math.Min(caret, _editor.Text.Length);
                    }
                    return true;
                }
            case 'p':
                {
                    string clip = SafeGetClipboardText();
                    if (string.IsNullOrEmpty(clip)) return true;
                    PushUndoSnapshot();
                    var t = _editor.Text ?? string.Empty;
                    int caret = _editor.CaretIndex;
                    bool lineMode = clip.EndsWith('\n');
                    if (lineMode)
                    {
                        int le = LineEnd(t, caret);
                        if (le < t.Length) le++; // include \n
                        string insertion = clip;
                        _editor.Text = t.Insert(le, insertion);
                        _editor.CaretIndex = Math.Min(le, _editor.Text.Length);
                    }
                    else
                    {
                        int at = caret < t.Length ? caret + 1 : t.Length;
                        _editor.Text = t.Insert(at, clip);
                        _editor.CaretIndex = at + clip.Length - 1;
                    }
                    return true;
                }
            case 'u':
                Undo();
                return true;
            case 'v':
                _visualAnchor = _editor.CaretIndex;
                SetMode(Mode.VisualChar);
                return true;
            case 'V':
                _visualAnchor = _editor.CaretIndex;
                SetMode(Mode.VisualLine);
                ExtendVisualSelection();
                return true;
            case ':':
                SetMode(Mode.Command);
                return true;
        }
        // 다른 모든 키는 가로채서 일반 TextBox 입력 방지
        return true;
    }

    private void MoveByVisualKey(char c)
    {
        var t = _editor.Text ?? string.Empty;
        int caret = _editor.CaretIndex;
        switch (c)
        {
            case 'h':
                if (caret > 0 && t[caret - 1] != '\n') _editor.CaretIndex = caret - 1;
                break;
            case 'l':
                if (caret < t.Length && t[caret] != '\n') _editor.CaretIndex = caret + 1;
                break;
            case 'j': MoveLine(+1); break;
            case 'k': MoveLine(-1); break;
            case '0': _editor.CaretIndex = LineStart(t, caret); break;
            case '$': _editor.CaretIndex = LineEnd(t, caret); break;
            case 'w': _editor.CaretIndex = NextWord(t, caret); break;
            case 'b': _editor.CaretIndex = PrevWord(t, caret); break;
        }
    }

    private void MoveCaret(Key k)
    {
        var t = _editor.Text ?? string.Empty;
        int caret = _editor.CaretIndex;
        switch (k)
        {
            case Key.Left: if (caret > 0) _editor.CaretIndex = caret - 1; break;
            case Key.Right: if (caret < t.Length) _editor.CaretIndex = caret + 1; break;
            case Key.Up: MoveLine(-1); break;
            case Key.Down: MoveLine(+1); break;
        }
    }

    private void MoveLine(int delta)
    {
        var t = _editor.Text ?? string.Empty;
        int caret = _editor.CaretIndex;
        int curLine = _editor.GetLineIndexFromCharacterIndex(caret);
        int col = caret - LineStart(t, caret);
        int target = curLine + delta;
        if (target < 0) target = 0;
        int total = _editor.LineCount;
        if (target >= total) target = total - 1;
        int targetStart = _editor.GetCharacterIndexFromLineIndex(target);
        int targetEnd = LineEnd(t, targetStart);
        int targetCaret = Math.Min(targetStart + col, targetEnd);
        _editor.CaretIndex = targetCaret;
    }

    private void ExtendVisualSelection()
    {
        var t = _editor.Text ?? string.Empty;
        int caret = _editor.CaretIndex;
        int from, to;
        if (CurrentMode == Mode.VisualLine)
        {
            int anchorLs = LineStart(t, _visualAnchor);
            int anchorLe = LineEnd(t, _visualAnchor);
            int curLs = LineStart(t, caret);
            int curLe = LineEnd(t, caret);
            from = Math.Min(anchorLs, curLs);
            to = Math.Max(anchorLe, curLe);
        }
        else
        {
            from = Math.Min(_visualAnchor, caret);
            to = Math.Max(_visualAnchor, caret);
            if (to < t.Length) to++; // 캐럿 글자 포함
        }
        _editor.Select(from, Math.Max(0, to - from));
        _editor.CaretIndex = caret;
    }

    private void DeleteSelection()
    {
        if (_editor.SelectionLength <= 0) return;
        PushUndoSnapshot();
        int start = _editor.SelectionStart;
        int len = _editor.SelectionLength;
        var t = _editor.Text ?? string.Empty;
        var yank = t.Substring(start, len);
        SafeSetClipboardText(CurrentMode == Mode.VisualLine && !yank.EndsWith('\n') ? yank + "\n" : yank);
        _editor.Text = t.Remove(start, len);
        _editor.CaretIndex = Math.Min(start, _editor.Text.Length);
    }

    private void YankSelection()
    {
        if (_editor.SelectionLength <= 0) return;
        int start = _editor.SelectionStart;
        int len = _editor.SelectionLength;
        var t = _editor.Text ?? string.Empty;
        var yank = t.Substring(start, len);
        SafeSetClipboardText(CurrentMode == Mode.VisualLine && !yank.EndsWith('\n') ? yank + "\n" : yank);
    }

    private void DeleteLine()
    {
        PushUndoSnapshot();
        var t = _editor.Text ?? string.Empty;
        int caret = _editor.CaretIndex;
        int ls = LineStart(t, caret);
        int le = LineEnd(t, caret);
        int end = le < t.Length ? le + 1 : le;  // \n 포함
        var yank = t.Substring(ls, end - ls);
        if (!yank.EndsWith('\n')) yank += "\n";
        SafeSetClipboardText(yank);
        _editor.Text = t.Remove(ls, end - ls);
        _editor.CaretIndex = Math.Min(ls, _editor.Text.Length);
    }

    private void YankLine()
    {
        var t = _editor.Text ?? string.Empty;
        int caret = _editor.CaretIndex;
        int ls = LineStart(t, caret);
        int le = LineEnd(t, caret);
        int end = le < t.Length ? le + 1 : le;
        var yank = t.Substring(ls, end - ls);
        if (!yank.EndsWith('\n')) yank += "\n";
        SafeSetClipboardText(yank);
    }

    private static string SafeGetClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty; }
        catch { return string.Empty; }
    }

    private static void SafeSetClipboardText(string s)
    {
        try { Clipboard.SetText(s); }
        catch { }
    }

    private static char? KeyToVisible(Key k, bool shift)
    {
        // 글자 키
        if (k >= Key.A && k <= Key.Z)
        {
            char baseCh = (char)('a' + (k - Key.A));
            return shift ? char.ToUpperInvariant(baseCh) : baseCh;
        }
        return k switch
        {
            Key.D0 => shift ? ')' : '0',
            Key.D4 => shift ? '$' : '4',
            Key.OemSemicolon => shift ? ':' : ';',
            _ => (char?)null,
        };
    }

    public static int LineStart(string text, int pos)
    {
        if (pos <= 0) return 0;
        int idx = text.LastIndexOf('\n', Math.Min(pos - 1, text.Length - 1));
        return idx == -1 ? 0 : idx + 1;
    }

    public static int LineEnd(string text, int pos)
    {
        if (pos >= text.Length) return text.Length;
        int idx = text.IndexOf('\n', pos);
        return idx == -1 ? text.Length : idx;
    }

    private static int NextWord(string t, int pos)
    {
        int n = t.Length;
        if (pos >= n) return n;
        bool startedOnWord = IsWordChar(t[pos]);
        int i = pos;
        // 현재 단어/구분자 무리 건너뛰기
        while (i < n && IsWordChar(t[i]) == startedOnWord && t[i] != '\n') i++;
        // 공백 건너뛰기
        while (i < n && (t[i] == ' ' || t[i] == '\t')) i++;
        return i;
    }

    private static int PrevWord(string t, int pos)
    {
        if (pos <= 0) return 0;
        int i = pos - 1;
        while (i > 0 && (t[i] == ' ' || t[i] == '\t')) i--;
        bool word = IsWordChar(t[i]);
        while (i > 0 && IsWordChar(t[i - 1]) == word && t[i - 1] != '\n') i--;
        return i;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private readonly record struct TextSnapshot(string Text, int Caret);
}
