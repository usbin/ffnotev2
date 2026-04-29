using System.Diagnostics;

namespace ffnotev2.Services;

public class GameDetectedEventArgs : EventArgs
{
    public string ProcessName { get; }
    public GameDetectedEventArgs(string processName) => ProcessName = processName;
}

public class GameDetectionService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly System.Threading.Timer _timer;
    private string? _lastDetected;

    public Func<IEnumerable<string>>? RegisteredProcessNamesProvider { get; set; }

    public event EventHandler<GameDetectedEventArgs>? GameDetected;

    public GameDetectionService()
    {
        _timer = new System.Threading.Timer(_ => Check(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start() => _timer.Change(TimeSpan.Zero, PollInterval);

    public void Stop() => _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private void Check()
    {
        var registered = RegisteredProcessNamesProvider?.Invoke();
        if (registered is null) return;

        var registeredSet = registered
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.ToLowerInvariant())
            .ToHashSet();
        if (registeredSet.Count == 0)
        {
            _lastDetected = null;
            return;
        }

        string? matched = null;
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (string.IsNullOrEmpty(proc.MainWindowTitle)) continue;
                var name = proc.ProcessName.ToLowerInvariant();
                if (registeredSet.Contains(name))
                {
                    matched = name;
                    break;
                }
            }
            catch
            {
                // 프로세스 접근 권한 등 예외는 무시
            }
            finally
            {
                proc.Dispose();
            }
        }

        if (matched is not null && matched != _lastDetected)
        {
            _lastDetected = matched;
            GameDetected?.Invoke(this, new GameDetectedEventArgs(matched));
        }
        else if (matched is null)
        {
            _lastDetected = null;
        }
    }

    public IReadOnlyList<(string ProcessName, string WindowTitle)> GetRunningWindowedProcesses()
    {
        var list = new List<(string, string)>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (string.IsNullOrEmpty(proc.MainWindowTitle)) continue;
                list.Add((proc.ProcessName, proc.MainWindowTitle));
            }
            catch
            {
            }
            finally
            {
                proc.Dispose();
            }
        }
        return list
            .OrderBy(t => t.Item1, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Dispose()
    {
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }
}
