using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record BundleResponse
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("zip_path")]
    public required string ZipPath { get; init; }

    [JsonPropertyName("size")]
    public required long Size { get; init; }

    [JsonPropertyName("download_command")]
    public required string DownloadCommand { get; init; }
}
