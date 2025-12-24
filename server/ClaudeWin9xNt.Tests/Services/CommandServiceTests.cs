using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ClaudeWin9xNtServer.Models.Requests;
using ClaudeWin9xNtServer.Models.Responses;
using ClaudeWin9xNtServer.Services;
using ClaudeWin9xNtServer.Services.Interfaces;

namespace ClaudeWin9xNtServer.Tests.Services;

public class CommandServiceTests
{
    private readonly ConcurrentDictionary<string, CommandRequest> _pendingCommands = new();
    private readonly ConcurrentDictionary<string, CommandResult> _commandResults = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandResult>> _commandWaiters = new();
    private readonly IApprovalService _approvalService = Substitute.For<IApprovalService>();
    private readonly ILogger<CommandService> _logger = Substitute.For<ILogger<CommandService>>();

    private CommandService CreateService(TimeSpan? timeout = null) =>
        new(_pendingCommands, _commandResults, _commandWaiters, _approvalService, _logger, timeout);

    [Fact]
    public void PollPendingCommand_WhenPendingCommandExists_ReturnsAndDispatchesCommand()
    {
        var service = CreateService();
        var command = new CommandRequest
        {
            Id = "cmd1",
            Command = "dir",
            WorkingDirectory = null,
            Status = "pending"
        };
        _pendingCommands.TryAdd("cmd1", command);

        var result = service.PollPendingCommand();

        result.ShouldNotBeNull();
        result.Id.ShouldBe("cmd1");
        result.Command.ShouldBe("dir");
        result.Status.ShouldBe("dispatched");
        _pendingCommands["cmd1"].Status.ShouldBe("dispatched");
    }

    [Fact]
    public void PollPendingCommand_SkipsAlreadyDispatchedCommands()
    {
        var service = CreateService();
        var dispatched = new CommandRequest
        {
            Id = "cmd1",
            Command = "dir",
            WorkingDirectory = null,
            Status = "dispatched"
        };
        var pending = new CommandRequest
        {
            Id = "cmd2",
            Command = "echo hello",
            WorkingDirectory = null,
            Status = "pending"
        };
        _pendingCommands.TryAdd("cmd1", dispatched);
        _pendingCommands.TryAdd("cmd2", pending);

        var result = service.PollPendingCommand();

        result.ShouldNotBeNull();
        result.Id.ShouldBe("cmd2");
    }

    [Fact]
    public void SubmitResult_AddsResultAndRemovesPendingCommand()
    {
        var service = CreateService();
        var command = new CommandRequest
        {
            Id = "cmd1",
            Command = "dir",
            WorkingDirectory = null,
            Status = "dispatched"
        };
        _pendingCommands.TryAdd("cmd1", command);

        var result = new CommandResult
        {
            CommandId = "cmd1",
            ExitCode = 0,
            Stdout = "output",
            Stderr = null
        };
        service.SubmitResult(result);

        _commandResults.ShouldContainKey("cmd1");
        _pendingCommands.ShouldNotContainKey("cmd1");
    }

    [Fact]
    public void GetCommandStatus_WhenResultExists_ReturnsResult()
    {
        var service = CreateService();
        var expected = new CommandResult
        {
            CommandId = "cmd1",
            ExitCode = 0,
            Stdout = "output",
            Stderr = "error"
        };
        _commandResults.TryAdd("cmd1", expected);

        var result = service.GetCommandStatus("cmd1");

        result.ShouldNotBeNull();
        result.ExitCode.ShouldBe(0);
        result.Stdout.ShouldBe("output");
        result.Stderr.ShouldBe("error");
    }

    [Fact]
    public void IsPending_WhenCommandIsPending_ReturnsTrue()
    {
        var service = CreateService();
        var command = new CommandRequest
        {
            Id = "cmd1",
            Command = "dir",
            WorkingDirectory = null,
            Status = "pending"
        };
        _pendingCommands.TryAdd("cmd1", command);

        var result = service.IsPending("cmd1");

        result.ShouldBeTrue();
    }

    [Fact]
    public void GetPendingStatus_ReturnsCorrectStatus()
    {
        var service = CreateService();
        var command = new CommandRequest
        {
            Id = "cmd1",
            Command = "dir",
            WorkingDirectory = null,
            Status = "dispatched"
        };
        _pendingCommands.TryAdd("cmd1", command);

        var result = service.GetPendingStatus("cmd1");

        result.ShouldBe("dispatched");
    }

    [Fact]
    public async Task QueueCommandAsync_WhenResultComesBack_ReturnsResult()
    {
        var service = CreateService(timeout: TimeSpan.FromSeconds(2));

        var queueTask = service.QueueCommandAsync("dir", null);

        await Task.Delay(100);
        var pending = service.PollPendingCommand();
        pending.ShouldNotBeNull();

        var result = new CommandResult
        {
            CommandId = pending.Id,
            ExitCode = 0,
            Stdout = "file1.txt\nfile2.txt",
            Stderr = null
        };
        service.SubmitResult(result);

        var commandResult = await queueTask;
        commandResult.ShouldNotBeNull();
        commandResult.ExitCode.ShouldBe(0);
        commandResult.Stdout.ShouldBe("file1.txt\nfile2.txt");
    }

    [Fact]
    public async Task QueueCommandAsync_WhenApproved_QueuesCommandAndReturnsResult()
    {
        var sessionId = "session1";

        // Configure mock to approve
        _approvalService.RequestApprovalAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var service = CreateService(timeout: TimeSpan.FromSeconds(2));
        var queueTask = service.QueueCommandAsync("dir", null, sessionId);

        // Wait for command to be queued and poll it
        var pendingCommand = await WaitForPendingCommandAsync(service);
        pendingCommand.ShouldNotBeNull();
        pendingCommand!.Id.ShouldNotBeNull();

        service.SubmitResult(new CommandResult
        {
            CommandId = pendingCommand.Id,
            ExitCode = 0,
            Stdout = "ok",
            Stderr = null
        });

        var result = await queueTask;
        result.ShouldNotBeNull();
        result.ExitCode.ShouldBe(0);
        result.Stdout.ShouldBe("ok");

        await _approvalService.Received(1).RequestApprovalAsync(
            sessionId,
            "Bash",
            "dir",
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueCommandAsync_WhenApprovalRejected_ReturnsRejectedResultAndDoesNotQueueCommand()
    {
        var sessionId = "session1";

        // Configure mock to reject
        _approvalService.RequestApprovalAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var service = CreateService(timeout: TimeSpan.FromSeconds(2));
        var result = await service.QueueCommandAsync("dir", null, sessionId);

        result.ShouldNotBeNull();
        result.ExitCode.ShouldBe(-1);
        result.Stderr.ShouldBe("Command rejected by user");
        _pendingCommands.ShouldBeEmpty();
    }

    [Fact]
    public async Task QueueCommandAsync_WhenTimeout_ReturnsNullAndRemovesPendingCommand()
    {
        var service = CreateService(timeout: TimeSpan.FromMilliseconds(100));

        var result = await service.QueueCommandAsync("dir", null);

        result.ShouldBeNull();
        _pendingCommands.ShouldBeEmpty();
    }


    private static async Task<CommandRequest?> WaitForPendingCommandAsync(CommandService service, int attempts = 50, int delayMs = 10)
    {
        for (var i = 0; i < attempts; i++)
        {
            var pending = service.PollPendingCommand();
            if (pending != null)
            {
                return pending;
            }
            await Task.Delay(delayMs);
        }

        return null;
    }
}
