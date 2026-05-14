using System.Text;
using ffnotev2.Models;
using Microsoft.Data.Sqlite;

namespace ffnotev2.Services;

/// <summary>
/// 모든 텍스트 노트의 마크다운 표를 메모리 SQLite DB에 동적 테이블로 매핑하고
/// 사용자 SQL 쿼리를 실행해 결과를 반환한다.
///
/// 테이블 이름 규칙: 마크다운 표 직전 헤딩(# / ## / ...) 텍스트.
///   - 헤딩 없는 표는 무시(이름 짓기 강제).
///   - 헤딩과 표 사이는 빈 줄 허용. 일반 텍스트가 끼면 헤딩 무효화.
///
/// 컬럼 이름: 헤더 셀 텍스트 그대로 (한글 가능).
/// 모든 값은 TEXT — 숫자 비교는 사용자가 CAST 사용.
///
/// 식별자는 모두 quoted("...") — 한글/특수문자 안전.
/// </summary>
public class QueryEngine : IDisposable
{
    private SqliteConnection? _conn;

    public void Rebuild(IEnumerable<NoteItem> notes)
    {
        _conn?.Dispose();
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in notes)
        {
            if (note.Type != NoteType.Text) continue;
            var content = note.Content;
            if (string.IsNullOrEmpty(content)) continue;
            ExtractAndCreate(_conn, content, seen);
        }
    }

    /// <summary>SQL을 실행해 (컬럼명, 행 데이터, 오류) 반환. SELECT 외 명령도 동작하지만 결과 없음.</summary>
    public (List<string> Columns, List<List<string>> Rows, string? Error) Execute(string sql)
    {
        if (_conn is null) return (new(), new(), "쿼리 엔진이 초기화되지 않았습니다.");
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            var cols = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++) cols.Add(reader.GetName(i));
            var rows = new List<List<string>>();
            while (reader.Read())
            {
                var row = new List<string>(reader.FieldCount);
                for (int i = 0; i < reader.FieldCount; i++)
                    row.Add(reader.IsDBNull(i) ? string.Empty : (reader.GetValue(i)?.ToString() ?? string.Empty));
                rows.Add(row);
            }
            return (cols, rows, null);
        }
        catch (Exception ex)
        {
            return (new(), new(), ex.Message);
        }
    }

    private static void ExtractAndCreate(SqliteConnection conn, string content, HashSet<string> seen)
    {
        var lines = content.Split('\n');
        string? heading = null;
        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                int j = 0;
                while (j < trimmed.Length && trimmed[j] == '#') j++;
                if (j > 0 && j < trimmed.Length && trimmed[j] == ' ')
                {
                    heading = trimmed.Substring(j + 1).Trim();
                    i++;
                    continue;
                }
            }
            if (IsTableRowLine(line) && i + 1 < lines.Length && IsSeparatorLine(lines[i + 1].TrimEnd('\r')))
            {
                var headerCells = SplitCells(line);
                var dataRows = new List<string[]>();
                int k = i + 2;
                while (k < lines.Length)
                {
                    var rl = lines[k].TrimEnd('\r');
                    if (!IsTableRowLine(rl) || IsSeparatorLine(rl)) break;
                    dataRows.Add(SplitCells(rl));
                    k++;
                }
                if (heading is not null && headerCells.Length > 0)
                {
                    var tableName = heading;
                    if (seen.Add(tableName))
                        CreateTable(conn, tableName, headerCells, dataRows);
                    // 같은 이름 중복은 첫 번째만 사용 — 사용자에게 명시적 충돌 책임
                }
                i = k;
                heading = null;
                continue;
            }
            if (!string.IsNullOrWhiteSpace(line)) heading = null;
            i++;
        }
    }

    private static bool IsTableRowLine(string line) =>
        line.Length >= 3 && line[0] == '|' && line[^1] == '|' && line.IndexOf('|', 1) < line.Length - 1;

    private static bool IsSeparatorLine(string line)
    {
        if (!IsTableRowLine(line)) return false;
        foreach (var ch in line) if (ch != '|' && ch != '-' && ch != ':' && ch != ' ') return false;
        return line.Contains('-');
    }

    private static string[] SplitCells(string row)
    {
        // |a|b|c| → ["a", "b", "c"] (양끝 '|' 제거 후 분할, 각 셀 trim)
        var inner = row.Substring(1, row.Length - 2);
        var parts = inner.Split('|');
        for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
        return parts;
    }

    private static void CreateTable(SqliteConnection conn, string name, string[] columns, List<string[]> rows)
    {
        var qname = Quote(name);
        // 컬럼명 중복 시 _2, _3 접미사
        var colNames = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in columns)
        {
            var raw = string.IsNullOrEmpty(c) ? "col" : c;
            if (counts.TryGetValue(raw, out var n)) { counts[raw] = n + 1; raw = raw + "_" + (n + 1); }
            else counts[raw] = 1;
            colNames.Add(raw);
        }
        var qcols = string.Join(", ", colNames.Select(Quote));

        using (var drop = conn.CreateCommand())
        {
            drop.CommandText = $"DROP TABLE IF EXISTS {qname}";
            drop.ExecuteNonQuery();
        }
        using (var create = conn.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {qname} ({qcols})";
            create.ExecuteNonQuery();
        }
        if (rows.Count == 0) return;

        using var tx = conn.BeginTransaction();
        using var insert = conn.CreateCommand();
        insert.Transaction = tx;
        var placeholders = string.Join(", ", Enumerable.Range(0, colNames.Count).Select(i => $"@p{i}"));
        insert.CommandText = $"INSERT INTO {qname} VALUES ({placeholders})";
        var parameters = new List<SqliteParameter>();
        for (int i = 0; i < colNames.Count; i++)
        {
            var p = insert.CreateParameter();
            p.ParameterName = $"@p{i}";
            insert.Parameters.Add(p);
            parameters.Add(p);
        }
        foreach (var row in rows)
        {
            for (int i = 0; i < colNames.Count; i++)
                parameters[i].Value = i < row.Length ? (object)row[i] : DBNull.Value;
            insert.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public void Dispose()
    {
        _conn?.Dispose();
        _conn = null;
        GC.SuppressFinalize(this);
    }
}
