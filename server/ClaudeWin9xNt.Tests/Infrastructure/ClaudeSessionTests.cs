using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ClaudeWin9xNtServer.Infrastructure;

namespace ClaudeWin9xNtServer.Tests.Infrastructure;

public class ClaudeSessionTests
{
    private readonly ILogger<ClaudeSession> _logger = Substitute.For<ILogger<ClaudeSession>>();
    private readonly string _tempWorkDir = Path.Combine(Path.GetTempPath(), $"session_{Guid.NewGuid()}");

    public ClaudeSessionTests()
    {
        Directory.CreateDirectory(_tempWorkDir);
    }

    private void Cleanup()
    {
        if (Directory.Exists(_tempWorkDir))
        {
            Directory.Delete(_tempWorkDir, recursive: true);
        }
    }

    [Fact]
    public void IsRunning_WhenNotStarted_ReturnsFalse()
    {
        using var session = new ClaudeSession("test1", _tempWorkDir, "Windows 98", _logger);

        session.IsRunning.ShouldBeFalse();
        Cleanup();
    }

    [Fact]
    public void SessionId_ReturnsProvidedId()
    {
        using var session = new ClaudeSession("mysession123", _tempWorkDir, "Windows XP", _logger);

        session.SessionId.ShouldBe("mysession123");
        Cleanup();
    }

    [Fact]
    public void LastActivity_IsSetToNowOnConstruction()
    {
        var before = DateTime.UtcNow;
        using var session = new ClaudeSession("test2", _tempWorkDir, "Windows 95", _logger);
        var after = DateTime.UtcNow;

        session.LastActivity.ShouldBeGreaterThanOrEqualTo(before);
        session.LastActivity.ShouldBeLessThanOrEqualTo(after);
        Cleanup();
    }

    [Fact]
    public void Stop_WhenNotRunning_DoesNotThrow()
    {
        using var session = new ClaudeSession("test5", _tempWorkDir, "Windows NT 4.0", _logger);

        session.Stop();
        session.IsRunning.ShouldBeFalse();
        Cleanup();
    }

    [Fact]
    public void Stop_CanBeCalledMultipleTimes()
    {
        using var session = new ClaudeSession("test6", _tempWorkDir, "Windows 95", _logger);

        session.Stop();
        session.Stop();
        session.Stop();

        session.IsRunning.ShouldBeFalse();
        Cleanup();
    }

    [Fact]
    public void Dispose_CallsStop()
    {
        var session = new ClaudeSession("test7", _tempWorkDir, "Windows 98", _logger);

        session.IsRunning.ShouldBeFalse();
        session.Dispose();

        session.Stop();
        Cleanup();
    }


    [Fact]
    public async Task SendInput_WhenNotRunning_DoesNotThrow()
    {
        using var session = new ClaudeSession("test13", _tempWorkDir, "Windows 2000", _logger);

        await session.SendInput("test message");

        Cleanup();
    }


    [Fact]
    public void Dispose_IsIdempotent()
    {
        var session = new ClaudeSession("test17", _tempWorkDir, "Windows 95", _logger);

        session.Dispose();
        session.Dispose();
        session.Dispose();

        session.IsRunning.ShouldBeFalse();
        Cleanup();
    }
}
