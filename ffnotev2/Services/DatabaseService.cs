using System.IO;
using ffnotev2.Models;
using Microsoft.Data.Sqlite;

namespace ffnotev2.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public string DataDirectory { get; }
    public string ImagesDirectory { get; }

    public DatabaseService()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ffnotev2");
        ImagesDirectory = Path.Combine(DataDirectory, "images");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ImagesDirectory);

        var dbPath = Path.Combine(DataDirectory, "notes.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        InitializeSchema();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }
        return conn;
    }

    private void InitializeSchema()
    {
        using var conn = Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS Notebooks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    ProcessName TEXT,
                    CreatedAt TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS NoteItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NotebookId INTEGER NOT NULL REFERENCES Notebooks(Id) ON DELETE CASCADE,
                    Type INTEGER NOT NULL,
                    Content TEXT NOT NULL,
                    X REAL NOT NULL,
                    Y REAL NOT NULL,
                    Width REAL NOT NULL,
                    Height REAL NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_NoteItems_NotebookId ON NoteItems(NotebookId);
                CREATE TABLE IF NOT EXISTS NoteGroups (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NotebookId INTEGER NOT NULL REFERENCES Notebooks(Id) ON DELETE CASCADE,
                    X REAL NOT NULL,
                    Y REAL NOT NULL,
                    Width REAL NOT NULL,
                    Height REAL NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_NoteGroups_NotebookId ON NoteGroups(NotebookId);
                """;
            cmd.ExecuteNonQuery();
        }

        // 마이그레이션: Notebooks.SnapEnabled (기존 DB에 컬럼 없으면 추가)
        if (!ColumnExists(conn, "Notebooks", "SnapEnabled"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE Notebooks ADD COLUMN SnapEnabled INTEGER NOT NULL DEFAULT 0";
            cmd.ExecuteNonQuery();
        }

        // 마이그레이션: Notebooks.OverlayDraft (노트북별 오버레이 초안 저장)
        if (!ColumnExists(conn, "Notebooks", "OverlayDraft"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE Notebooks ADD COLUMN OverlayDraft TEXT NOT NULL DEFAULT ''";
            cmd.ExecuteNonQuery();
        }
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public List<NoteBook> GetNotebooks()
    {
        var notebooks = new List<NoteBook>();
        using var conn = Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Name, ProcessName, CreatedAt, SnapEnabled, OverlayDraft FROM Notebooks ORDER BY Id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                notebooks.Add(new NoteBook
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    ProcessName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CreatedAt = DateTime.Parse(reader.GetString(3)),
                    SnapEnabled = reader.GetInt32(4) != 0,
                    OverlayDraft = reader.IsDBNull(5) ? string.Empty : reader.GetString(5)
                });
            }
        }

        foreach (var nb in notebooks)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT Id, Type, Content, X, Y, Width, Height, CreatedAt, UpdatedAt
                    FROM NoteItems WHERE NotebookId = $nb ORDER BY Id
                    """;
                cmd.Parameters.AddWithValue("$nb", nb.Id);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    nb.Notes.Add(new NoteItem
                    {
                        Id = reader.GetInt32(0),
                        NotebookId = nb.Id,
                        Type = (NoteType)reader.GetInt32(1),
                        Content = reader.GetString(2),
                        X = reader.GetDouble(3),
                        Y = reader.GetDouble(4),
                        Width = reader.GetDouble(5),
                        Height = reader.GetDouble(6),
                        CreatedAt = DateTime.Parse(reader.GetString(7)),
                        UpdatedAt = DateTime.Parse(reader.GetString(8))
                    });
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, X, Y, Width, Height FROM NoteGroups WHERE NotebookId = $nb ORDER BY Id";
                cmd.Parameters.AddWithValue("$nb", nb.Id);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    nb.Groups.Add(new NoteGroup
                    {
                        Id = reader.GetInt32(0),
                        NotebookId = nb.Id,
                        X = reader.GetDouble(1),
                        Y = reader.GetDouble(2),
                        Width = reader.GetDouble(3),
                        Height = reader.GetDouble(4)
                    });
                }
            }
        }

        return notebooks;
    }

    public int AddGroup(NoteGroup g)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NoteGroups (NotebookId, X, Y, Width, Height)
            VALUES ($nb, $x, $y, $w, $h);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$nb", g.NotebookId);
        cmd.Parameters.AddWithValue("$x", g.X);
        cmd.Parameters.AddWithValue("$y", g.Y);
        cmd.Parameters.AddWithValue("$w", g.Width);
        cmd.Parameters.AddWithValue("$h", g.Height);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void UpdateGroup(NoteGroup g)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE NoteGroups SET X=$x, Y=$y, Width=$w, Height=$h WHERE Id=$id";
        cmd.Parameters.AddWithValue("$x", g.X);
        cmd.Parameters.AddWithValue("$y", g.Y);
        cmd.Parameters.AddWithValue("$w", g.Width);
        cmd.Parameters.AddWithValue("$h", g.Height);
        cmd.Parameters.AddWithValue("$id", g.Id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteGroup(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM NoteGroups WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public int CreateNotebook(string name, string? processName = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Notebooks (Name, ProcessName, CreatedAt)
            VALUES ($name, $proc, $created);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$proc", (object?)processName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("o"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void RenameNotebook(int id, string name)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Notebooks SET Name = $name WHERE Id = $id";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetNotebookProcess(int id, string? processName)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Notebooks SET ProcessName = $proc WHERE Id = $id";
        cmd.Parameters.AddWithValue("$proc", (object?)processName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetNotebookSnapEnabled(int id, bool enabled)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Notebooks SET SnapEnabled = $v WHERE Id = $id";
        cmd.Parameters.AddWithValue("$v", enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetNotebookOverlayDraft(int id, string draft)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Notebooks SET OverlayDraft = $v WHERE Id = $id";
        cmd.Parameters.AddWithValue("$v", draft ?? string.Empty);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteNotebook(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Notebooks WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public int AddNote(NoteItem note)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NoteItems (NotebookId, Type, Content, X, Y, Width, Height, CreatedAt, UpdatedAt)
            VALUES ($nb, $type, $content, $x, $y, $w, $h, $created, $updated);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$nb", note.NotebookId);
        cmd.Parameters.AddWithValue("$type", (int)note.Type);
        cmd.Parameters.AddWithValue("$content", note.Content);
        cmd.Parameters.AddWithValue("$x", note.X);
        cmd.Parameters.AddWithValue("$y", note.Y);
        cmd.Parameters.AddWithValue("$w", note.Width);
        cmd.Parameters.AddWithValue("$h", note.Height);
        cmd.Parameters.AddWithValue("$created", note.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$updated", note.UpdatedAt.ToString("o"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void UpdateNote(NoteItem note)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE NoteItems
            SET Content = $content, X = $x, Y = $y, Width = $w, Height = $h, UpdatedAt = $updated
            WHERE Id = $id
            """;
        cmd.Parameters.AddWithValue("$content", note.Content);
        cmd.Parameters.AddWithValue("$x", note.X);
        cmd.Parameters.AddWithValue("$y", note.Y);
        cmd.Parameters.AddWithValue("$w", note.Width);
        cmd.Parameters.AddWithValue("$h", note.Height);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$id", note.Id);
        cmd.ExecuteNonQuery();
    }

    public void UpdateNotePosition(int id, double x, double y)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE NoteItems SET X = $x, Y = $y, UpdatedAt = $updated WHERE Id = $id
            """;
        cmd.Parameters.AddWithValue("$x", x);
        cmd.Parameters.AddWithValue("$y", y);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteNote(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM NoteItems WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
