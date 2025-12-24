using System.Collections.Concurrent;
using ClaudeWin9xNtServer.Infrastructure;
using ClaudeWin9xNtServer.Models.Requests;
using ClaudeWin9xNtServer.Models.Responses;
using ClaudeWin9xNtServer.Services.Interfaces;

namespace ClaudeWin9xNtServer.Services;

public class CommandService(
    ConcurrentDictionary<string, CommandRequest> pendingCommands,
    ConcurrentDictionary<string, CommandResult> commandResults,
    ConcurrentDictionary<string, TaskCompletionSource<CommandResult>> commandWaiters,
    IApprovalService approvalService,
    ILogger<CommandService> logger,
    TimeSpan? timeout = null) : ICommandService
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(120);

    public async Task<CommandResult?> QueueCommandAsync(string command, string? workingDirectory, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        if (sessionId != null)
        {
            var approved = await approvalService.RequestApprovalAsync(
                sessionId,
                "Bash",
                command,
                _timeout,
                cancellationToken);

            if (!approved)
            {
                logger.LogWarning("Command rejected by user: {Command}", command);
                return new CommandResult
                {
                    CommandId = "rejected",
                    ExitCode = -1,
                    Stdout = "",
                    Stderr = "Command rejected by user"
                };
            }
        }

        var cmdId = IdGenerator.NewId();

        var request = new CommandRequest
        {
            Id = cmdId,
            Command = command,
            WorkingDirectory = workingDirectory,
            SessionId = sessionId,
            Status = "pending"
        };

        var tcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!commandWaiters.TryAdd(cmdId, tcs))
        {
            logger.LogError("Failed to register waiter for command {CommandId}", cmdId);
            return null;
        }

        if (!pendingCommands.TryAdd(cmdId, request))
        {
            commandWaiters.TryRemove(cmdId, out _);
            logger.LogError("Failed to queue command {CommandId} (duplicate)", cmdId);
            return null;
        }

        logger.LogInformation("Queued command {CommandId}: {Command}", cmdId, command);

        try
        {
            var result = await tcs.Task.WaitAsync(_timeout, cancellationToken);
            logger.LogInformation("Command {CommandId} completed: exit={ExitCode}, stdout={StdoutLength} chars",
                cmdId, result.ExitCode, result.Stdout?.Length ?? 0);
            return result;
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Timeout waiting for command {CommandId}", cmdId);
            return null;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Command cancelled {CommandId}", cmdId);
            return null;
        }
        finally
        {
            commandWaiters.TryRemove(cmdId, out _);
            pendingCommands.TryRemove(cmdId, out _);
        }
    }

    public CommandRequest? PollPendingCommand()
    {
        var pending = pendingCommands.Values
            .Where(c => c.Status == "pending")
            .OrderBy(c => c.Id)
            .FirstOrDefault();

        if (pending == null)
        {
            return null;
        }

        var dispatched = pending with { Status = "dispatched" };
        if (!pendingCommands.TryUpdate(pending.Id!, dispatched, pending))
        {
            logger.LogWarning("Failed to dispatch command {CommandId} (concurrent modification)", pending.Id);
            return null;
        }

        logger.LogInformation("Dispatched command {CommandId} to client: {Command}", pending.Id, pending.Command);
        return dispatched;
    }

    public void SubmitResult(CommandResult result)
    {
        if (string.IsNullOrEmpty(result.CommandId))
        {
            return;
        }

        logger.LogInformation("Result received for {CommandId}: exit={ExitCode}", result.CommandId, result.ExitCode);

        commandResults.TryAdd(result.CommandId, result);

        if (commandWaiters.TryRemove(result.CommandId, out var tcs))
        {
            tcs.TrySetResult(result);
        }

        pendingCommands.TryRemove(result.CommandId, out _);
    }

    public CommandResult? GetCommandStatus(string commandId) =>
        commandResults.TryGetValue(commandId, out var result) ? result : null;

    public bool IsPending(string commandId) => pendingCommands.ContainsKey(commandId);

    public string? GetPendingStatus(string commandId) => pendingCommands.TryGetValue(commandId, out var pending) ? pending.Status : null;
}
