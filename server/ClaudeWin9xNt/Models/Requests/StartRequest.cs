using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Requests;

public record StartRequest
{
    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; init; }

    [JsonPropertyName("windows_version")]
    public string? WindowsVersion { get; init; }
}
