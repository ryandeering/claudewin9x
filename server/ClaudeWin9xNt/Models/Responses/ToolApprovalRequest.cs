using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record ToolApprovalRequest
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("tool_name")]
    public required string ToolName { get; init; }

    [JsonPropertyName("tool_input")]
    public string? ToolInput { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}
