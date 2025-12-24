using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record FileOperation
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}
