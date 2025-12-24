using System.Text.Json.Serialization;

namespace ClaudeWin9xNtServer.Models.Responses;

public record ApprovalPollResponse
{
    [JsonPropertyName("has_pending")]
    public required bool HasPending { get; init; }

    [JsonPropertyName("approval_id")]
    public string? ApprovalId { get; init; }

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    [JsonPropertyName("tool_input")]
    public string? ToolInput { get; init; }
}
