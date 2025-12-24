using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record FileReadResponse
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonPropertyName("total_size")]
    public required int TotalSize { get; init; }
}
