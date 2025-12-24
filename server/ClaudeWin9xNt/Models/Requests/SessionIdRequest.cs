using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Requests;

public record SessionIdRequest
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }
}
