using ClaudeWin9xNtServer.Models.Responses;

namespace ClaudeWin9xNtServer.Services.Interfaces;

public interface ISessionService
{
    (string SessionId, string Status) StartSession(string? workingDirectory, string? windowsVersion);
    Task<bool> SendInput(string sessionId, string text);
    (string Output, string Status)? GetOutput(string sessionId);
    bool StopSession(string sessionId);
    SessionInfo[] ListSessions();
}
