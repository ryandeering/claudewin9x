using System.Collections.Concurrent;
using ClaudeWin9xNtServer.Infrastructure;
using ClaudeWin9xNtServer.Models.Responses;
using ClaudeWin9xNtServer.Services.Interfaces;

namespace ClaudeWin9xNtServer.Services;

public class SessionService(
    ILogger<SessionService> logger,
    int heartbeatTimeoutSeconds = 180) : ISessionService, IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, ClaudeSession> _sessions = new();
    private readonly int _heartbeatTimeoutSeconds = heartbeatTimeoutSeconds;
    private CancellationTokenSource? _cleanupCts;
    private Task? _cleanupTask;

    public ConcurrentDictionary<string, ClaudeSession> Sessions => _sessions;

    public (string SessionId, string Status) StartSession(string? workingDirectory, string? windowsVersion)
    {
        var workingDir = workingDirectory ?? Path.GetTempPath();
        var winVersion = windowsVersion ?? "Unknown Windows";

        logger.LogInformation("Client connecting: {WindowsVersion}", winVersion);

        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var session = new ClaudeSession(sessionId, workingDir, winVersion, logger);

        if (_sessions.TryAdd(sessionId, session))
        {
            session.Start();
            logger.LogInformation("Session {SessionId} started for {WindowsVersion}", sessionId, winVersion);
            return (sessionId, "running");
        }

        logger.LogError("Failed to create session (duplicate ID): {SessionId}", sessionId);
        throw new InvalidOperationException("Failed to create session");
    }

    public async Task<bool> SendInput(string sessionId, string text)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        await session.SendInput(text);
        return true;
    }

    public (string Output, string Status)? GetOutput(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        session.UpdateHeartbeat();
        var output = session.GetParsedOutput();
        var status = session.IsRunning ? "running" : "stopped";
        return (output, status);
    }

    public bool StopSession(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
        {
            return false;
        }

        session.Stop();
        logger.LogInformation("Session {SessionId} stopped", sessionId);
        return true;
    }

    public SessionInfo[] ListSessions()
    {
        return [.. _sessions.Select(s => new SessionInfo
        {
            SessionId = s.Key,
            Status = s.Value.IsRunning ? "running" : "stopped",
            LastActivity = s.Value.LastActivity.ToString("o")
        })];
    }

    public bool TryGetSession(string sessionId, out ClaudeSession? session) => _sessions.TryGetValue(sessionId, out session);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cleanupTask = RunCleanupAsync(_cleanupCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cleanupCts?.Cancel();

        if (_cleanupTask != null)
        {
            await _cleanupTask;
        }

        foreach (var session in _sessions.Values)
        {
            session.Stop();
        }
        _sessions.Clear();
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

                var now = DateTime.UtcNow;
                var stale = _sessions
                    .Where(s => (now - s.Value.LastActivity).TotalSeconds > _heartbeatTimeoutSeconds)
                    .ToList();

                foreach (var s in stale)
                {
                    if (_sessions.TryRemove(s.Key, out var session))
                    {
                        logger.LogWarning("Session {SessionId} timed out (no heartbeat for {TimeoutSeconds}s), stopping",
                            s.Key, _heartbeatTimeoutSeconds);
                        session.Stop();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _cleanupCts?.Cancel();
        _cleanupCts?.Dispose();

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();
    }
}
