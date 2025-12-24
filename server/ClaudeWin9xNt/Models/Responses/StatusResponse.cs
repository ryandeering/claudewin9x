using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record StatusResponse
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }
}
