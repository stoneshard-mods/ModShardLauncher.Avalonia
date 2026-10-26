using Avalonia.Media;
using Avalonia.Media.Immutable;
using Serilog.Core;
using Serilog.Events;

namespace ModShardLauncher.GUI.Services;

/// <summary>One line of the in-app log panel.</summary>
public sealed record LogEntry(DateTime Time, LogEventLevel Level, string Message)
{
    // Shared immutable brushes: the log panel renders thousands of rows, and creating a new
    // SolidColorBrush per row per render is pure garbage.
    private static readonly IBrush DebugBrush = Brushes.Gray;
    private static readonly IBrush InformationBrush = new ImmutableSolidColorBrush(Color.Parse("#7FB4FF"));
    private static readonly IBrush WarningBrush = new ImmutableSolidColorBrush(Color.Parse("#E8B85C"));
    private static readonly IBrush ErrorBrush = new ImmutableSolidColorBrush(Color.Parse("#F06A6A"));

    public string TimeText => Time.ToString("HH:mm:ss");

    public string LevelText => Level switch
    {
        LogEventLevel.Verbose => "VRB",
        LogEventLevel.Debug => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning => "WRN",
        LogEventLevel.Error => "ERR",
        _ => "FTL",
    };

    public IBrush LevelBrush => Level switch
    {
        LogEventLevel.Verbose or LogEventLevel.Debug => DebugBrush,
        LogEventLevel.Information => InformationBrush,
        LogEventLevel.Warning => WarningBrush,
        _ => ErrorBrush,
    };
}

/// <summary>
/// Serilog sink that forwards formatted events to the UI. The replacement for the old console
/// window: everything the WPF build printed to a hidden console now lands in the log panel.
/// </summary>
public sealed class UiLogSink : ILogEventSink
{
    private const int HistoryLimit = 500;

    private readonly object _gate = new();
    private readonly List<LogEntry> _history = new();
    private LogEventLevel _minimumLevel = LogEventLevel.Information;

    /// <summary>Raised on the thread that produced the event; consumers must marshal to the UI thread.</summary>
    public event Action<LogEntry>? EntryWritten;

    /// <summary>Events emitted before the shell subscribed (startup lines).</summary>
    public IReadOnlyList<LogEntry> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToArray();
            }
        }
    }

    public void SetMinimumLevel(LogEventLevel level) => _minimumLevel = level;

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < _minimumLevel)
        {
            return;
        }

        LogEntry entry = new(
            logEvent.Timestamp.LocalDateTime,
            logEvent.Level,
            logEvent.RenderMessage());

        lock (_gate)
        {
            _history.Add(entry);
            if (_history.Count > HistoryLimit)
            {
                _history.RemoveAt(0);
            }
        }

        EntryWritten?.Invoke(entry);
    }
}
