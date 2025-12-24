using System.Collections.Concurrent;
using ClaudeWin9xNtServer.Infrastructure;
using ClaudeWin9xNtServer.Models.Responses;
using ClaudeWin9xNtServer.Services.Interfaces;

namespace ClaudeWin9xNtServer.Services;

public class ApprovalService(
    ConcurrentDictionary<string, ToolApprovalRequest> pendingApprovals,
    ConcurrentDictionary<string, TaskCompletionSource<bool>> approvalWaiters,
    ILogger<ApprovalService> logger) : IApprovalService
{
    public async Task<bool> RequestApprovalAsync(string sessionId, string toolName, string toolInput, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var approvalId = IdGenerator.NewId();

        var request = new ToolApprovalRequest
        {
            Id = approvalId,
            SessionId = sessionId,
            ToolName = toolName,
            ToolInput = toolInput,
            Status = "pending"
        };

        if (!pendingApprovals.TryAdd(approvalId, request))
        {
            logger.LogError("Failed to queue approval {ApprovalId} (duplicate)", approvalId);
            return false;
        }

        logger.LogInformation("Queued approval {ApprovalId}: {ToolName} - {ToolInput}",
            approvalId, toolName, toolInput.Length > 100 ? toolInput[..100] + "..." : toolInput);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!approvalWaiters.TryAdd(approvalId, tcs))
        {
            pendingApprovals.TryRemove(approvalId, out _);
            logger.LogError("Failed to register waiter for approval {ApprovalId}", approvalId);
            return false;
        }

        try
        {
            return await tcs.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Timeout waiting for approval {ApprovalId}", approvalId);
            return false;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Approval cancelled {ApprovalId}", approvalId);
            return false;
        }
        finally
        {
            approvalWaiters.TryRemove(approvalId, out _);
            pendingApprovals.TryRemove(approvalId, out _);
        }
    }

    public ToolApprovalRequest? PollPendingApproval(string sessionId)
    {
        return pendingApprovals.Values
            .FirstOrDefault(a => a.SessionId == sessionId && a.Status == "pending");
    }

    public bool SubmitResponse(string approvalId, bool approved)
    {
        if (!pendingApprovals.TryGetValue(approvalId, out var approval))
        {
            return false;
        }

        var newStatus = approved ? "approved" : "rejected";
        if (!pendingApprovals.TryUpdate(approvalId, approval with { Status = newStatus }, approval))
        {
            logger.LogWarning("Failed to update approval status for {ApprovalId}", approvalId);
        }

        logger.LogInformation("Approval {ApprovalId}: {ToolName} -> {Status}", approvalId, approval.ToolName, newStatus);

        if (approvalWaiters.TryRemove(approvalId, out var tcs))
        {
            tcs.TrySetResult(approved);
        }

        return true;
    }
}
