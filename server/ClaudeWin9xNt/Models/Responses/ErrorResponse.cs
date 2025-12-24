using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record ErrorResponse
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }
}
