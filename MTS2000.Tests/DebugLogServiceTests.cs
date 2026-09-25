using System.IO;
using MTS2000.App.Services;

namespace MTS2000.Tests;

public sealed class DebugLogServiceTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), $"mts2000-log-{Guid.NewGuid():N}");

    [Fact]
    public void Write_CreatesDailyLogWithMessageAndException()
    {
        var logger = new DebugLogService(_logDirectory);
        logger.Info("test action");
        logger.Error("test failure", new InvalidOperationException("test detail"));

        var logPath = Directory.GetFiles(_logDirectory, "*.log").Single();
        var contents = File.ReadAllText(logPath);

        Assert.Contains("[INFO] test action", contents);
        Assert.Contains("[ERROR] test failure", contents);
        Assert.Contains("InvalidOperationException: test detail", contents);
    }

    [Fact]
    public void ReadCurrentLog_ReturnsRecentTailWithinLimit()
    {
        var logger = new DebugLogService(_logDirectory);
        for (var index = 0; index < 100; index++)
        {
            logger.Info($"entry-{index:D3}-{new string('x', 40)}");
        }

        var contents = logger.ReadCurrentLog(200);

        Assert.True(contents.Length <= 200);
        Assert.Contains("entry-099", contents);
        Assert.DoesNotContain("entry-000", contents);
    }

    public void Dispose()
    {
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }
}