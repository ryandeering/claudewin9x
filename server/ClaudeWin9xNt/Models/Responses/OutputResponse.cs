using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record OutputResponse
{
    [JsonPropertyName("output")]
    public required string Output { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}
