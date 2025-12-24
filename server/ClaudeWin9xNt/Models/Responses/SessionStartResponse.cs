using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record SessionStartResponse
{
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}
