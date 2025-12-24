using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record FileWriteResponse
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("bytes_written")]
    public required int BytesWritten { get; init; }
}
