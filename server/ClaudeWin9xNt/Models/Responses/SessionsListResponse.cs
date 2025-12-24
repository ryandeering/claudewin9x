namespace ClaudeWin9xNtServer.Models.Responses;

public record SessionsListResponse
{
    public required SessionInfo[] Sessions { get; init; }
}
