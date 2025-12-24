using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record CommandPollResponse
{
    [JsonPropertyName("has_pending")]
    public required bool HasPending { get; init; }

    [JsonPropertyName("cmd_id")]
    public string? CmdId { get; init; }

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; init; }
}
