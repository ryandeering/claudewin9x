using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record FileOpResult
{
    [JsonPropertyName("op_id")]
    public string? OpId { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("entries")]
    public List<FileEntry>? Entries { get; init; }
}
