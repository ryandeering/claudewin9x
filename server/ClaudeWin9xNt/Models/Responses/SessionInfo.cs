namespace ClaudeWin9xNtServer.Models.Responses;

public record SessionInfo
{
    public required string SessionId { get; init; }
    public required string Status { get; init; }
    public required string LastActivity { get; init; }
}
