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
        using var cmd = conn.CreateCommand();
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
            """;
        cmd.ExecuteNonQuery();
    }

    public List<NoteBook> GetNotebooks()
    {
        var notebooks = new List<NoteBook>();
        using var conn = Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Name, ProcessName, CreatedAt FROM Notebooks ORDER BY Id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                notebooks.Add(new NoteBook
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    ProcessName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CreatedAt = DateTime.Parse(reader.GetString(3))
                });
            }
        }

        foreach (var nb in notebooks)
        {
            using var cmd = conn.CreateCommand();
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

        return notebooks;
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
