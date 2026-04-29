using Microsoft.Data.Sqlite;
using ffnotev2.Models;
using System.Collections.ObjectModel;

namespace ffnotev2.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ffnotev2");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "notes.db");
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Notebooks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ProcessName TEXT,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS NoteItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NotebookId INTEGER NOT NULL,
                Type TEXT NOT NULL,
                Content TEXT,
                X REAL NOT NULL DEFAULT 100,
                Y REAL NOT NULL DEFAULT 100,
                Width REAL NOT NULL DEFAULT 220,
                Height REAL NOT NULL DEFAULT 130,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (NotebookId) REFERENCES Notebooks(Id) ON DELETE CASCADE
            );";
        cmd.ExecuteNonQuery();
    }

    public List<NoteBook> LoadAllNotebooks()
    {
        using var conn = OpenConnection();
        var notebooks = new List<NoteBook>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Name, ProcessName, CreatedAt FROM Notebooks ORDER BY CreatedAt";
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
            cmd.CommandText = "SELECT Id, NotebookId, Type, Content, X, Y, Width, Height, CreatedAt, UpdatedAt FROM NoteItems WHERE NotebookId = @id";
            cmd.Parameters.AddWithValue("@id", nb.Id);
            using var reader = cmd.ExecuteReader();
            nb.Notes = new ObservableCollection<NoteItem>();
            while (reader.Read())
            {
                nb.Notes.Add(new NoteItem
                {
                    Id = reader.GetInt32(0),
                    NotebookId = reader.GetInt32(1),
                    Type = Enum.Parse<NoteType>(reader.GetString(2)),
                    Content = reader.IsDBNull(3) ? null : reader.GetString(3),
                    X = reader.GetDouble(4),
                    Y = reader.GetDouble(5),
                    Width = reader.GetDouble(6),
                    Height = reader.GetDouble(7),
                    CreatedAt = DateTime.Parse(reader.GetString(8)),
                    UpdatedAt = DateTime.Parse(reader.GetString(9))
                });
            }
        }

        return notebooks;
    }

    public int SaveNotebook(NoteBook notebook)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        if (notebook.Id == 0)
        {
            cmd.CommandText = "INSERT INTO Notebooks (Name, ProcessName, CreatedAt) VALUES (@n, @p, @c); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@n", notebook.Name);
            cmd.Parameters.AddWithValue("@p", (object?)notebook.ProcessName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@c", notebook.CreatedAt.ToString("o"));
            notebook.Id = (int)(long)cmd.ExecuteScalar()!;
        }
        else
        {
            cmd.CommandText = "UPDATE Notebooks SET Name=@n, ProcessName=@p WHERE Id=@id";
            cmd.Parameters.AddWithValue("@n", notebook.Name);
            cmd.Parameters.AddWithValue("@p", (object?)notebook.ProcessName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", notebook.Id);
            cmd.ExecuteNonQuery();
        }
        return notebook.Id;
    }

    public void DeleteNotebook(int id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Notebooks WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public int SaveNoteItem(NoteItem item)
    {
        using var conn = OpenConnection();
        item.UpdatedAt = DateTime.Now;
        using var cmd = conn.CreateCommand();
        if (item.Id == 0)
        {
            cmd.CommandText = @"INSERT INTO NoteItems (NotebookId, Type, Content, X, Y, Width, Height, CreatedAt, UpdatedAt)
                                VALUES (@nb, @t, @c, @x, @y, @w, @h, @ca, @ua); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@nb", item.NotebookId);
            cmd.Parameters.AddWithValue("@t", item.Type.ToString());
            cmd.Parameters.AddWithValue("@c", (object?)item.Content ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@x", item.X);
            cmd.Parameters.AddWithValue("@y", item.Y);
            cmd.Parameters.AddWithValue("@w", item.Width);
            cmd.Parameters.AddWithValue("@h", item.Height);
            cmd.Parameters.AddWithValue("@ca", item.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("@ua", item.UpdatedAt.ToString("o"));
            item.Id = (int)(long)cmd.ExecuteScalar()!;
        }
        else
        {
            cmd.CommandText = "UPDATE NoteItems SET Content=@c, X=@x, Y=@y, Width=@w, Height=@h, UpdatedAt=@ua WHERE Id=@id";
            cmd.Parameters.AddWithValue("@c", (object?)item.Content ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@x", item.X);
            cmd.Parameters.AddWithValue("@y", item.Y);
            cmd.Parameters.AddWithValue("@w", item.Width);
            cmd.Parameters.AddWithValue("@h", item.Height);
            cmd.Parameters.AddWithValue("@ua", item.UpdatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("@id", item.Id);
            cmd.ExecuteNonQuery();
        }
        return item.Id;
    }

    public void DeleteNoteItem(int id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM NoteItems WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void UpdateNotePosition(int id, double x, double y)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE NoteItems SET X=@x, Y=@y, UpdatedAt=@ua WHERE Id=@id";
        cmd.Parameters.AddWithValue("@x", x);
        cmd.Parameters.AddWithValue("@y", y);
        cmd.Parameters.AddWithValue("@ua", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
