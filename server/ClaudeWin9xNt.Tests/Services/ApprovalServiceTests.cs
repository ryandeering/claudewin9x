using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ClaudeWin9xNtServer.Models.Responses;
using ClaudeWin9xNtServer.Services;

namespace ClaudeWin9xNtServer.Tests.Services;

public class ApprovalServiceTests
{
    private readonly ConcurrentDictionary<string, ToolApprovalRequest> _pendingApprovals = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _approvalWaiters = new();
    private readonly ILogger<ApprovalService> _logger = Substitute.For<ILogger<ApprovalService>>();

    private ApprovalService CreateService() =>
        new(_pendingApprovals, _approvalWaiters, _logger);

    [Fact]
    public void PollPendingApproval_WhenPendingApprovalExists_ReturnsApproval()
    {
        var service = CreateService();
        var approval = new ToolApprovalRequest
        {
            Id = "app1",
            SessionId = "session1",
            ToolName = "Bash",
            ToolInput = "dir",
            Status = "pending"
        };
        _pendingApprovals.TryAdd("app1", approval);

        var result = service.PollPendingApproval("session1");

        result.ShouldNotBeNull();
        result.Id.ShouldBe("app1");
        result.ToolName.ShouldBe("Bash");
        result.ToolInput.ShouldBe("dir");
    }

    [Fact]
    public void PollPendingApproval_FiltersOnSessionId()
    {
        var service = CreateService();
        var approval1 = new ToolApprovalRequest
        {
            Id = "app1",
            SessionId = "session1",
            ToolName = "Bash",
            ToolInput = "dir",
            Status = "pending"
        };
        var approval2 = new ToolApprovalRequest
        {
            Id = "app2",
            SessionId = "session2",
            ToolName = "Write",
            ToolInput = "test.txt",
            Status = "pending"
        };
        _pendingApprovals.TryAdd("app1", approval1);
        _pendingApprovals.TryAdd("app2", approval2);

        var result = service.PollPendingApproval("session2");

        result.ShouldNotBeNull();
        result.Id.ShouldBe("app2");
        result.SessionId.ShouldBe("session2");
    }

    [Fact]
    public void PollPendingApproval_SkipsNonPendingApprovals()
    {
        var service = CreateService();
        var approved = new ToolApprovalRequest
        {
            Id = "app1",
            SessionId = "session1",
            ToolName = "Bash",
            ToolInput = "dir",
            Status = "approved"
        };
        var pending = new ToolApprovalRequest
        {
            Id = "app2",
            SessionId = "session1",
            ToolName = "Write",
            ToolInput = "test.txt",
            Status = "pending"
        };
        _pendingApprovals.TryAdd("app1", approved);
        _pendingApprovals.TryAdd("app2", pending);

        var result = service.PollPendingApproval("session1");

        result.ShouldNotBeNull();
        result.Id.ShouldBe("app2");
    }

    [Fact]
    public void PollPendingApproval_WhenNoMatchingApproval_ReturnsNull()
    {
        var service = CreateService();

        var result = service.PollPendingApproval("nonexistent");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task SubmitResponse_WhenApproved_UpdatesStatusAndSignalsWaiter()
    {
        var service = CreateService();
        var approval = new ToolApprovalRequest
        {
            Id = "app1",
            SessionId = "session1",
            ToolName = "Bash",
            ToolInput = "dir",
            Status = "pending"
        };
        _pendingApprovals.TryAdd("app1", approval);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _approvalWaiters.TryAdd("app1", tcs);

        var result = service.SubmitResponse("app1", approved: true);

        result.ShouldBeTrue();
        _pendingApprovals["app1"].Status.ShouldBe("approved");
        tcs.Task.IsCompleted.ShouldBeTrue();
        (await tcs.Task).ShouldBeTrue();
    }

    [Fact]
    public async Task SubmitResponse_WhenRejected_UpdatesStatusAndSignalsWaiter()
    {
        var service = CreateService();
        var approval = new ToolApprovalRequest
        {
            Id = "app1",
            SessionId = "session1",
            ToolName = "Bash",
            ToolInput = "dir",
            Status = "pending"
        };
        _pendingApprovals.TryAdd("app1", approval);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _approvalWaiters.TryAdd("app1", tcs);

        var result = service.SubmitResponse("app1", approved: false);

        result.ShouldBeTrue();
        _pendingApprovals["app1"].Status.ShouldBe("rejected");
        tcs.Task.IsCompleted.ShouldBeTrue();
        (await tcs.Task).ShouldBeFalse();
    }

    [Fact]
    public void SubmitResponse_WhenApprovalNotFound_ReturnsFalse()
    {
        var service = CreateService();

        var result = service.SubmitResponse("nonexistent", approved: true);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task RequestApprovalAsync_WhenApproved_ReturnsTrue()
    {
        var service = CreateService();

        var approvalTask = service.RequestApprovalAsync(
            "session1",
            "Bash",
            "dir",
            TimeSpan.FromSeconds(2));

        await Task.Delay(50);
        var pending = service.PollPendingApproval("session1");
        pending.ShouldNotBeNull();

        service.SubmitResponse(pending.Id, approved: true);

        var result = await approvalTask;
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task RequestApprovalAsync_WhenRejected_ReturnsFalse()
    {
        var service = CreateService();

        var approvalTask = service.RequestApprovalAsync(
            "session1",
            "Bash",
            "rm -rf /",
            TimeSpan.FromSeconds(2));

        await Task.Delay(50);
        var pending = service.PollPendingApproval("session1");
        pending.ShouldNotBeNull();

        service.SubmitResponse(pending.Id, approved: false);

        var result = await approvalTask;
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task RequestApprovalAsync_WhenTimeout_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.RequestApprovalAsync(
            "session1",
            "Bash",
            "dir",
            TimeSpan.FromMilliseconds(100));

        result.ShouldBeFalse();
        _pendingApprovals.ShouldBeEmpty();
        _approvalWaiters.ShouldBeEmpty();
    }

    [Fact]
    public async Task RequestApprovalAsync_WhenCancelled_ReturnsFalse()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        var approvalTask = service.RequestApprovalAsync(
            "session1",
            "Bash",
            "dir",
            TimeSpan.FromSeconds(2),
            cts.Token);

        await Task.Delay(50);
        cts.Cancel();

        var result = await approvalTask;
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task RequestApprovalAsync_CleansUpAfterCompletion()
    {
        var service = CreateService();

        var approvalTask = service.RequestApprovalAsync(
            "session1",
            "Bash",
            "dir",
            TimeSpan.FromSeconds(2));

        await Task.Delay(50);
        var pending = service.PollPendingApproval("session1");
        pending.ShouldNotBeNull();

        service.SubmitResponse(pending.Id, approved: true);
        await approvalTask;

        _pendingApprovals.ShouldBeEmpty();
        _approvalWaiters.ShouldBeEmpty();
    }

    [Fact]
    public async Task RequestApprovalAsync_TruncatesLongToolInput()
    {
        var service = CreateService();
        var longInput = new string('x', 200);

        var approvalTask = service.RequestApprovalAsync(
            "session1",
            "Bash",
            longInput,
            TimeSpan.FromSeconds(2));

        await Task.Delay(50);
        var pending = service.PollPendingApproval("session1");
        pending.ShouldNotBeNull();
        pending.ToolInput.ShouldBe(longInput);

        service.SubmitResponse(pending.Id, approved: true);
        await approvalTask;
    }
}
