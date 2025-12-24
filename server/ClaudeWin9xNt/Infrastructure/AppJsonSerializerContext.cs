using System.Text.Json.Serialization;
using ClaudeWin9xNtServer.Models.Requests;
using ClaudeWin9xNtServer.Models.Responses;

namespace ClaudeWin9xNtServer.Infrastructure;

/// <summary>
/// Source-generated JSON serializer for Native AOT. AOT can't use reflection, so all
/// serializable types must be registered here. Add new request/response types or JSON
/// serialization will fail at runtime.
/// See: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation
/// </summary>
[JsonSerializable(typeof(StartRequest))]
[JsonSerializable(typeof(InputRequest))]
[JsonSerializable(typeof(SessionIdRequest))]
[JsonSerializable(typeof(CommandRequest))]
[JsonSerializable(typeof(CommandResult))]
[JsonSerializable(typeof(BundleRequest))]
[JsonSerializable(typeof(FileWriteRequest))]
[JsonSerializable(typeof(FileOperation))]
[JsonSerializable(typeof(FileOpResult))]
[JsonSerializable(typeof(FileEntry))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(SessionStartResponse))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(OutputResponse))]
[JsonSerializable(typeof(SessionsListResponse))]
[JsonSerializable(typeof(CommandQueueResponse))]
[JsonSerializable(typeof(CommandPollResponse))]
[JsonSerializable(typeof(CommandStatusResponse))]
[JsonSerializable(typeof(BundleResponse))]
[JsonSerializable(typeof(DirectoryListResponse))]
[JsonSerializable(typeof(FileReadResponse))]
[JsonSerializable(typeof(FileWriteResponse))]
[JsonSerializable(typeof(FileOpPollResponse))]
[JsonSerializable(typeof(ApprovalPollResponse))]
[JsonSerializable(typeof(ApprovalResponse))]
[JsonSerializable(typeof(ToolApprovalRequest))]
[JsonSerializable(typeof(SessionInfo))]
[JsonSerializable(typeof(SessionInfo[]))]
[JsonSerializable(typeof(List<FileEntry>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
