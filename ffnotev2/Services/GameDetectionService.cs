using ffnotev2.Models;
using System.Diagnostics;

namespace ffnotev2.Services;

public class GameDetectedEventArgs(NoteBook notebook) : EventArgs
{
    public NoteBook Notebook { get; } = notebook;
}

public class ProcessInfo
{
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public override string ToString() => $"{WindowTitle}  ({ProcessName})";
}

public class GameDetectionService : IDisposable
{
    private Timer? _timer;
    private List<NoteBook> _notebooks = [];
    private int? _lastDetectedId;

    public event EventHandler<GameDetectedEventArgs>? GameDetected;

    public void UpdateNotebooks(IEnumerable<NoteBook> notebooks)
    {
        _notebooks = [.. notebooks];
    }

    public void Start()
    {
        _timer = new Timer(Check, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    public void Stop() => _timer?.Dispose();

    private void Check(object? _)
    {
        var running = Process.GetProcesses()
            .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
            .Select(p => p.ProcessName.ToLowerInvariant())
            .ToHashSet();

        var matched = _notebooks.FirstOrDefault(nb =>
            !string.IsNullOrEmpty(nb.ProcessName) &&
            running.Contains(nb.ProcessName.ToLowerInvariant()));

        if (matched != null && matched.Id != _lastDetectedId)
        {
            _lastDetectedId = matched.Id;
            GameDetected?.Invoke(this, new GameDetectedEventArgs(matched));
        }
        else if (matched == null)
        {
            _lastDetectedId = null;
        }
    }

    public static List<ProcessInfo> GetRunningWindowedProcesses()
    {
        return [.. Process.GetProcesses()
            .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
            .Select(p => new ProcessInfo { ProcessName = p.ProcessName, WindowTitle = p.MainWindowTitle })
            .OrderBy(p => p.WindowTitle)];
    }

    public void Dispose() => Stop();
}
