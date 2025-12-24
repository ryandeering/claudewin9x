using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Requests;

public record CommandRequest
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}
