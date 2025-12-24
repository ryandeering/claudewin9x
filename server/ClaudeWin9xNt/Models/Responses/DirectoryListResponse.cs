using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record DirectoryListResponse
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("entries")]
    public required List<FileEntry> Entries { get; init; }
}
