using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Requests;

public record BundleRequest
{
    [JsonPropertyName("source_path")]
    public string? SourcePath { get; init; }

    [JsonPropertyName("output_name")]
    public string? OutputName { get; init; }
}
