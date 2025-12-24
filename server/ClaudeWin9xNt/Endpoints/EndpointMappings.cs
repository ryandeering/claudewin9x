using Microsoft.AspNetCore.Http.HttpResults;
using ClaudeWin9xNtServer.Models.Requests;
using ClaudeWin9xNtServer.Models.Responses;
using ClaudeWin9xNtServer.Services.Interfaces;
using System.Diagnostics.CodeAnalysis;

namespace ClaudeWin9xNtServer.Endpoints;

public static class EndpointMappings
{
    [RequiresUnreferencedCode("ASP.NET Core minimal APIs may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("ASP.NET Core minimal APIs may require runtime code generation")]
    public static void MapProxyEndpoints(this WebApplication app)
    {
        app.MapGet("/", () => TypedResults.Ok("Claude Win9x/NT server is running."));

        app.MapSessionEndpoints();

        app.MapCommandEndpoints();

        app.MapFilesystemEndpoints();

        app.MapApprovalEndpoints();
    }

    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(String, Delegate)")]
    private static void MapSessionEndpoints(this WebApplication app)
    {
        app.MapPost("/start", Results<Ok<SessionStartResponse>, StatusCodeHttpResult> (StartRequest request, ISessionService sessionService) =>
        {
            try
            {
                var (sessionId, status) = sessionService.StartSession(request.WorkingDirectory, request.WindowsVersion);
                return TypedResults.Ok(new SessionStartResponse { SessionId = sessionId, Status = status });
            }
            catch (InvalidOperationException)
            {
                return TypedResults.StatusCode(500);
            }
        });

        app.MapPost("/input", async Task<Results<Ok<StatusResponse>, NotFound<ErrorResponse>>> (InputRequest request, ISessionService sessionService) =>
        {
            if (request.SessionId == null)
            {
                return TypedResults.NotFound(new ErrorResponse { Error = "Session not found" });
            }

            var success = await sessionService.SendInput(request.SessionId, request.Text ?? "");
            if (!success)
            {
                return TypedResults.NotFound(new ErrorResponse { Error = "Session not found" });
            }

            return TypedResults.Ok(new StatusResponse { Status = "ok" });
        });

        app.MapGet("/output", Results<Ok<OutputResponse>, NotFound<ErrorResponse>> (string session_id, ISessionService sessionService) =>
        {
            var result = sessionService.GetOutput(session_id);
            if (result == null)
            {
                return TypedResults.NotFound(new ErrorResponse { Error = "Session not found" });
            }

            return TypedResults.Ok(new OutputResponse { Output = result.Value.Output, Status = result.Value.Status });
        });

        app.MapPost("/stop", Results<Ok<StatusResponse>, NotFound<ErrorResponse>> (SessionIdRequest request, ISessionService sessionService) =>
        {
            if (request.SessionId == null || !sessionService.StopSession(request.SessionId))
            {
                return TypedResults.NotFound(new ErrorResponse { Error = "Session not found" });
            }

            return TypedResults.Ok(new StatusResponse { Status = "stopped" });
        });

        app.MapGet("/sessions", (ISessionService sessionService) =>
        {
            var sessions = sessionService.ListSessions();
            return TypedResults.Ok(new SessionsListResponse { Sessions = sessions });
        });

    }

    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(String, Delegate)")]
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(String, Delegate)")]
    private static void MapCommandEndpoints(this WebApplication app)
    {
        app.MapPost("/cmd/queue", async Task<Results<Ok<CommandQueueResponse>, BadRequest<ErrorResponse>, StatusCodeHttpResult>> (CommandRequest request, ICommandService commandService) =>
        {
            if (string.IsNullOrEmpty(request.Command))
            {
                return TypedResults.BadRequest(new ErrorResponse { Error = "Command is required" });
            }

            var result = await commandService.QueueCommandAsync(request.Command, request.WorkingDirectory, request.SessionId);

            if (result == null)
            {
                return TypedResults.StatusCode(504);
            }

            return TypedResults.Ok(new CommandQueueResponse
            {
                CommandId = result.CommandId,
                Status = "completed",
                ExitCode = result.ExitCode,
                Stdout = result.Stdout,
                Stderr = result.Stderr
            });
        });

        app.MapGet("/cmd/poll", (ICommandService commandService) =>
        {
            var pending = commandService.PollPendingCommand();

            if (pending == null)
            {
                return TypedResults.Ok(new CommandPollResponse { HasPending = false });
            }

            return TypedResults.Ok(new CommandPollResponse
            {
                HasPending = true,
                CmdId = pending.Id,
                Command = pending.Command,
                WorkingDirectory = pending.WorkingDirectory
            });
        });

        app.MapPost("/cmd/result", Results<Ok<StatusResponse>, BadRequest<ErrorResponse>> (CommandResult result, ICommandService commandService) =>
        {
            if (string.IsNullOrEmpty(result.CommandId))
            {
                return TypedResults.BadRequest(new ErrorResponse { Error = "command_id is required" });
            }

            commandService.SubmitResult(result);
            return TypedResults.Ok(new StatusResponse { Status = "ok" });
        });

        app.MapGet("/cmd/status", Results<Ok<CommandStatusResponse>, NotFound<CommandStatusResponse>> (string command_id, ICommandService commandService) =>
        {
            var result = commandService.GetCommandStatus(command_id);

            if (result != null)
            {
                return TypedResults.Ok(new CommandStatusResponse
                {
                    Status = "completed",
                    ExitCode = result.ExitCode,
                    Stdout = result.Stdout,
                    Stderr = result.Stderr
                });
            }

            if (commandService.IsPending(command_id))
            {
                return TypedResults.Ok(new CommandStatusResponse { Status = commandService.GetPendingStatus(command_id) });
            }

            return TypedResults.NotFound(new CommandStatusResponse { Status = "not_found" });
        });
    }

    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(String, Delegate)")]
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(String, Delegate)")]
    private static void MapFilesystemEndpoints(this WebApplication app)
    {
        app.MapPost("/fs/bundle", Results<Ok<BundleResponse>, BadRequest<ErrorResponse>, StatusCodeHttpResult> (BundleRequest request, IFileSystemService fileSystemService) =>
        {
            if (string.IsNullOrEmpty(request.SourcePath))
            {
                return TypedResults.BadRequest(new ErrorResponse { Error = "source_path is required" });
            }

            var result = fileSystemService.CreateBundle(request.SourcePath, request.OutputName);

            if (result == null)
            {
                return TypedResults.StatusCode(500);
            }

            var outputName = request.OutputName ?? "bundle.zip";
            return TypedResults.Ok(new BundleResponse
            {
                Status = "ok",
                ZipPath = result.Value.ZipPath,
                Size = result.Value.Size,
                DownloadCommand = $"/download {Path.GetFileName(result.Value.ZipPath)} C:\\\\{outputName}"
            });
        });

        app.MapGet("/fs/list", async Task<Results<Ok<DirectoryListResponse>, StatusCodeHttpResult>> (string path, IFileSystemService fileSystemService) =>
        {
            var result = await fileSystemService.ListDirectoryAsync(path);

            if (result == null)
            {
                return TypedResults.StatusCode(504);
            }

            if (result.Error != null)
            {
                return TypedResults.StatusCode(500);
            }

            return TypedResults.Ok(new DirectoryListResponse { Path = path, Entries = result.Entries ?? [] });
        });

        app.MapGet("/fs/read", async Task<Results<Ok<FileReadResponse>, StatusCodeHttpResult>> (string path, int? maxSize, IFileSystemService fileSystemService) =>
        {
            var result = await fileSystemService.ReadFileAsync(path, maxSize);

            if (result == null)
            {
                return TypedResults.StatusCode(504);
            }

            return TypedResults.Ok(new FileReadResponse
            {
                Path = path,
                Content = result.Value.Content,
                Truncated = result.Value.Truncated,
                TotalSize = result.Value.TotalSize
            });
        });

        app.MapPost("/fs/write", async Task<Results<Ok<FileWriteResponse>, BadRequest<ErrorResponse>, StatusCodeHttpResult>> (FileWriteRequest request, IFileSystemService fileSystemService) =>
        {
            if (string.IsNullOrEmpty(request.Path) || request.Content == null)
            {
                return TypedResults.BadRequest(new ErrorResponse { Error = "path and content are required" });
            }

            var success = await fileSystemService.WriteFileAsync(request.Path, request.Content, request.SessionId);

            if (!success)
            {
                return TypedResults.StatusCode(504);
            }

            return TypedResults.Ok(new FileWriteResponse { Status = "ok", Path = request.Path, BytesWritten = request.Content.Length });
        });

        app.MapGet("/fs/poll", (IFileSystemService fileSystemService) =>
        {
            var pending = fileSystemService.PollPendingOperation();

            if (pending == null)
            {
                return TypedResults.Ok(new FileOpPollResponse { HasPending = false });
            }

            return TypedResults.Ok(new FileOpPollResponse
            {
                HasPending = true,
                OpId = pending.Id,
                Operation = pending.Operation,
                Path = pending.Path,
                Content = pending.Content
            });
        });

        app.MapPost("/fs/result", Results<Ok<StatusResponse>, BadRequest<ErrorResponse>> (FileOpResult result, IFileSystemService fileSystemService) =>
        {
            if (string.IsNullOrEmpty(result.OpId))
            {
                return TypedResults.BadRequest(new ErrorResponse { Error = "op_id is required" });
            }

            fileSystemService.SubmitResult(result);
            return TypedResults.Ok(new StatusResponse { Status = "ok" });
        });
    }

    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    private static void MapApprovalEndpoints(this WebApplication app)
    {
        app.MapGet("/approval/poll", Results<Ok<ApprovalPollResponse>, BadRequest<ErrorResponse>> (string session_id, IApprovalService approvalService) =>
        {
            if (string.IsNullOrEmpty(session_id))
            {
                return TypedResults.BadRequest(new ErrorResponse { Error = "session_id is required" });
            }

            var pending = approvalService.PollPendingApproval(session_id);

            if (pending == null)
            {
                return TypedResults.Ok(new ApprovalPollResponse { HasPending = false });
            }

            return TypedResults.Ok(new ApprovalPollResponse
            {
                HasPending = true,
                ApprovalId = pending.Id,
                ToolName = pending.ToolName,
                ToolInput = pending.ToolInput
            });
        });

        app.MapPost("/approval/respond", Results<Ok<StatusResponse>, BadRequest<ErrorResponse>, NotFound<ErrorResponse>> (ApprovalResponse response, IApprovalService approvalService) =>
        {
            if (string.IsNullOrEmpty(response.ApprovalId))
            {
                return TypedResults.BadRequest(new ErrorResponse { Error = "approval_id is required" });
            }

            var success = approvalService.SubmitResponse(response.ApprovalId, response.Approved);

            if (!success)
            {
                return TypedResults.NotFound(new ErrorResponse { Error = "Approval request not found" });
            }

            return TypedResults.Ok(new StatusResponse { Status = "ok" });
        });
    }
}
