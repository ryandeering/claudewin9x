using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record FileOpPollResponse
{
    [JsonPropertyName("has_pending")]
    public required bool HasPending { get; init; }

    [JsonPropertyName("op_id")]
    public string? OpId { get; init; }

    [JsonPropertyName("operation")]
    public string? Operation { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }
}
