namespace ClaudeWin9xNtServer.Infrastructure;

internal static class IdGenerator
{
    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}
