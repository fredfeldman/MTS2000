using System.Globalization;
using System.IO;
using System.Text;

namespace MTS2000.App.Services;

/// <summary>Writes diagnostic actions and outcomes to a per-day application log.</summary>
public sealed class DebugLogService
{
    private static readonly object SyncRoot = new();

    private readonly string _logDirectory;

    public DebugLogService(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MTS2000",
            "logs");
    }

    public string LogDirectory => _logDirectory;

    public string CurrentLogPath => Path.Combine(_logDirectory, $"mts2000-{DateTime.Now:yyyy-MM-dd}.log");

    public event EventHandler? LogWritten;

    public void Write(string level, string message, Exception? exception = null)
    {
        try
        {
            var timestamp = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
            var entry = new StringBuilder()
                .Append(timestamp)
                .Append(" [")
                .Append(level)
                .Append("] ")
                .Append(message);

            if (exception is not null)
            {
                entry.Append(" | ")
                    .Append(exception.GetType().Name)
                    .Append(": ")
                    .Append(exception.Message);
            }

            entry.AppendLine();
            Directory.CreateDirectory(_logDirectory);
            lock (SyncRoot)
            {
                File.AppendAllText(CurrentLogPath, entry.ToString(), Encoding.UTF8);
            }

            LogWritten?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Diagnostics must never interfere with the application workflow.
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Warning(string message) => Write("WARN", message);

    public void Error(string message, Exception exception) => Write("ERROR", message, exception);

    public string ReadCurrentLog(int maxCharacters = 100_000)
    {
        try
        {
            if (!File.Exists(CurrentLogPath))
            {
                return "No log entries for today.";
            }

            var lines = new Queue<string>();
            var characterCount = 0;
            foreach (var line in File.ReadLines(CurrentLogPath, Encoding.UTF8))
            {
                lines.Enqueue(line);
                characterCount += line.Length + Environment.NewLine.Length;
                while (characterCount > maxCharacters && lines.Count > 1)
                {
                    characterCount -= lines.Dequeue().Length + Environment.NewLine.Length;
                }
            }

            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return $"Unable to read the debug log: {ex.Message}";
        }
    }
}
