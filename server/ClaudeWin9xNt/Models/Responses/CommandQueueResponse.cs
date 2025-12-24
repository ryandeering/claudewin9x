using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record CommandQueueResponse
{
    [JsonPropertyName("command_id")]
    public string? CommandId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("exit_code")]
    public int? ExitCode { get; init; }

    [JsonPropertyName("stdout")]
    public string? Stdout { get; init; }

    [JsonPropertyName("stderr")]
    public string? Stderr { get; init; }
}
