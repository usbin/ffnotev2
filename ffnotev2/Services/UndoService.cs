using ffnotev2.Models;

namespace ffnotev2.Services;

public interface IUndoableAction
{
    void Undo();
    void Redo();
}

/// <summary>
/// 단순 LIFO 기반 Undo/Redo 스택. 새 액션 Push 시 Redo 스택은 비워짐.
/// 한 액션은 보통 사용자의 한 동작 단위 (드래그 1회, 리사이즈 1회, 추가 1회 등).
/// </summary>
public class UndoService
{
    private const int MaxStackSize = 200;
    private readonly LinkedList<IUndoableAction> _undo = new();
    private readonly LinkedList<IUndoableAction> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(IUndoableAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _undo.AddLast(action);
        if (_undo.Count > MaxStackSize) _undo.RemoveFirst();
        _redo.Clear();
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var a = _undo.Last!.Value;
        _undo.RemoveLast();
        a.Undo();
        _redo.AddLast(a);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var a = _redo.Last!.Value;
        _redo.RemoveLast();
        a.Redo();
        _undo.AddLast(a);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

// ───────── 액션 정의들 ─────────

public record ItemSnapshot(object Target, double X, double Y, double W, double H);

/// <summary>위치/크기 변경(드래그/리사이즈/nudge/그룹 동기 이동) 통합 undo 액션.</summary>
public class TransformItemsAction : IUndoableAction
{
    private readonly List<(ItemSnapshot Old, ItemSnapshot New)> _items;
    private readonly DatabaseService _db;

    public TransformItemsAction(IEnumerable<(ItemSnapshot Old, ItemSnapshot New)> items, DatabaseService db)
    {
        _items = items.ToList();
        _db = db;
    }

    public void Undo() => Apply(useNew: false);
    public void Redo() => Apply(useNew: true);

    private void Apply(bool useNew)
    {
        foreach (var (oldS, newS) in _items)
        {
            var s = useNew ? newS : oldS;
            switch (s.Target)
            {
                case NoteItem n:
                    n.X = s.X; n.Y = s.Y; n.Width = s.W; n.Height = s.H;
                    _db.UpdateNote(n);
                    break;
                case NoteGroup g:
                    g.X = s.X; g.Y = s.Y; g.Width = s.W; g.Height = s.H;
                    _db.UpdateGroup(g);
                    break;
            }
        }
    }
}

public class AddNoteAction : IUndoableAction
{
    private readonly NoteItem _note;
    private readonly NoteBook _notebook;
    private readonly DatabaseService _db;

    public AddNoteAction(NoteItem note, NoteBook notebook, DatabaseService db)
    {
        _note = note; _notebook = notebook; _db = db;
    }

    public void Undo()
    {
        _db.DeleteNote(_note.Id);
        _notebook.Notes.Remove(_note);
    }

    public void Redo()
    {
        _note.Id = _db.AddNote(_note);
        _notebook.Notes.Add(_note);
    }
}

public class DeleteNoteAction : IUndoableAction
{
    private readonly NoteItem _note;
    private readonly NoteBook _notebook;
    private readonly DatabaseService _db;

    public DeleteNoteAction(NoteItem note, NoteBook notebook, DatabaseService db)
    {
        _note = note; _notebook = notebook; _db = db;
    }

    public void Undo()
    {
        _note.Id = _db.AddNote(_note);
        _notebook.Notes.Add(_note);
    }

    public void Redo()
    {
        _db.DeleteNote(_note.Id);
        _notebook.Notes.Remove(_note);
    }
}

public class AddGroupAction : IUndoableAction
{
    private readonly NoteGroup _group;
    private readonly NoteBook _notebook;
    private readonly DatabaseService _db;

    public AddGroupAction(NoteGroup group, NoteBook notebook, DatabaseService db)
    {
        _group = group; _notebook = notebook; _db = db;
    }

    public void Undo()
    {
        _db.DeleteGroup(_group.Id);
        _notebook.Groups.Remove(_group);
    }

    public void Redo()
    {
        _group.Id = _db.AddGroup(_group);
        _notebook.Groups.Add(_group);
    }
}

public class DeleteGroupAction : IUndoableAction
{
    private readonly NoteGroup _group;
    private readonly NoteBook _notebook;
    private readonly DatabaseService _db;

    public DeleteGroupAction(NoteGroup group, NoteBook notebook, DatabaseService db)
    {
        _group = group; _notebook = notebook; _db = db;
    }

    public void Undo()
    {
        _group.Id = _db.AddGroup(_group);
        _notebook.Groups.Add(_group);
    }

    public void Redo()
    {
        _db.DeleteGroup(_group.Id);
        _notebook.Groups.Remove(_group);
    }
}
