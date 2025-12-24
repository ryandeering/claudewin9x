using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ClaudeWin9xNtServer.Models.Responses;
using ClaudeWin9xNtServer.Services;
using ClaudeWin9xNtServer.Services.Interfaces;

namespace ClaudeWin9xNtServer.Tests.Services;

public class FileSystemServiceTests
{
    private readonly ConcurrentDictionary<string, FileOperation> _pendingFileOps = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<FileOpResult>> _fileOpWaiters = new();
    private readonly IApprovalService _approvalService = Substitute.For<IApprovalService>();
    private readonly ILogger<FileSystemService> _logger = Substitute.For<ILogger<FileSystemService>>();

    private FileSystemService CreateService(TimeSpan? readTimeout = null, TimeSpan? writeTimeout = null) =>
        new(_pendingFileOps, _fileOpWaiters, _approvalService, _logger, readTimeout, writeTimeout);

    [Fact]
    public void PollPendingOperation_WhenPendingOpExists_ReturnsAndDispatchesOp()
    {
        var service = CreateService();
        var op = new FileOperation
        {
            Id = "op1",
            Operation = "read",
            Path = "C:\\test.txt",
            Content = null,
            Status = "pending"
        };
        _pendingFileOps.TryAdd("op1", op);

        var result = service.PollPendingOperation();

        result.ShouldNotBeNull();
        result.Id.ShouldBe("op1");
        result.Operation.ShouldBe("read");
        result.Path.ShouldBe("C:\\test.txt");
        _pendingFileOps["op1"].Status.ShouldBe("dispatched");
    }

    [Fact]
    public void PollPendingOperation_SkipsDispatchedOps()
    {
        var service = CreateService();
        var dispatched = new FileOperation
        {
            Id = "op1",
            Operation = "read",
            Path = "C:\\a.txt",
            Content = null,
            Status = "dispatched"
        };
        var pending = new FileOperation
        {
            Id = "op2",
            Operation = "list",
            Path = "C:\\folder",
            Content = null,
            Status = "pending"
        };
        _pendingFileOps.TryAdd("op1", dispatched);
        _pendingFileOps.TryAdd("op2", pending);

        var result = service.PollPendingOperation();

        result.ShouldNotBeNull();
        result.Id.ShouldBe("op2");
    }

    [Fact]
    public void SubmitResult_AddsResultAndRemovesPendingOp()
    {
        var service = CreateService();
        var op = new FileOperation
        {
            Id = "op1",
            Operation = "read",
            Path = "C:\\test.txt",
            Content = null,
            Status = "dispatched"
        };
        _pendingFileOps.TryAdd("op1", op);

        var result = new FileOpResult
        {
            OpId = "op1",
            Error = null,
            Content = "file content",
            Entries = null
        };
        service.SubmitResult(result);

        _pendingFileOps.ShouldNotContainKey("op1");
    }

    [Fact]
    public async Task ListDirectoryAsync_WhenResultComesBack_ReturnsResult()
    {
        var service = CreateService(readTimeout: TimeSpan.FromSeconds(2));

        var listTask = service.ListDirectoryAsync("C:\\");

        // Simulate Win98 client polling and returning result
        await Task.Delay(100);
        var pending = service.PollPendingOperation();
        pending.ShouldNotBeNull();
        pending.Operation.ShouldBe("list");

        var entries = new List<FileEntry>
        {
            new() { Name = "file1.txt", Type = "file", Size = 1024 },
            new() { Name = "folder", Type = "dir", Size = 0 }
        };
        var result = new FileOpResult
        {
            OpId = pending.Id,
            Error = null,
            Content = null,
            Entries = entries
        };
        service.SubmitResult(result);

        var listResult = await listTask;
        listResult.ShouldNotBeNull();
        listResult.Entries.ShouldNotBeNull();
        listResult.Entries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ReadFileAsync_WhenResultComesBack_ReturnsContent()
    {
        var service = CreateService(readTimeout: TimeSpan.FromSeconds(2));

        var readTask = service.ReadFileAsync("C:\\test.txt");

        await Task.Delay(100);
        var pending = service.PollPendingOperation();
        pending.ShouldNotBeNull();

        var result = new FileOpResult
        {
            OpId = pending.Id,
            Error = null,
            Content = "Hello, World!",
            Entries = null
        };
        service.SubmitResult(result);

        var readResult = await readTask;
        readResult.ShouldNotBeNull();
        readResult.Value.Content.ShouldBe("Hello, World!");
        readResult.Value.Truncated.ShouldBeFalse();
        readResult.Value.TotalSize.ShouldBe(13);
    }

    [Fact]
    public async Task ReadFileAsync_WhenContentExceedsMaxSize_TruncatesContent()
    {
        var service = CreateService(readTimeout: TimeSpan.FromSeconds(5));

        var readTask = service.ReadFileAsync("C:\\test.txt", maxSize: 5);

        await Task.Delay(100);
        var pending = service.PollPendingOperation();
        pending.ShouldNotBeNull();

        var result = new FileOpResult
        {
            OpId = pending.Id,
            Error = null,
            Content = "Hello, World!",
            Entries = null
        };
        service.SubmitResult(result);

        var readResult = await readTask;
        readResult.ShouldNotBeNull();
        readResult.Value.Content.ShouldBe("Hello");
        readResult.Value.Truncated.ShouldBeTrue();
        readResult.Value.TotalSize.ShouldBe(13);
    }

    [Fact]
    public async Task WriteFileAsync_WhenSuccess_ReturnsTrue()
    {
        var service = CreateService(writeTimeout: TimeSpan.FromSeconds(2));

        var writeTask = service.WriteFileAsync("C:\\test.txt", "new content");

        await Task.Delay(100);
        var pending = service.PollPendingOperation();
        pending.ShouldNotBeNull();
        pending.Operation.ShouldBe("write");
        pending.Content.ShouldBe("new content");

        var result = new FileOpResult
        {
            OpId = pending.Id,
            Error = null,
            Content = null,
            Entries = null
        };
        service.SubmitResult(result);

        var writeResult = await writeTask;
        writeResult.ShouldBeTrue();
    }

    [Fact]
    public async Task WriteFileAsync_WhenError_ReturnsFalse()
    {
        var service = CreateService(writeTimeout: TimeSpan.FromSeconds(2));

        var writeTask = service.WriteFileAsync("C:\\readonly.txt", "content");

        await Task.Delay(100);
        var pending = service.PollPendingOperation();
        pending.ShouldNotBeNull();

        var result = new FileOpResult
        {
            OpId = pending.Id,
            Error = "Access denied",
            Content = null,
            Entries = null
        };
        service.SubmitResult(result);

        var writeResult = await writeTask;
        writeResult.ShouldBeFalse();
    }

    [Fact]
    public async Task WriteFileAsync_WhenApproved_QueuesOperationAndReturnsTrue()
    {
        var sessionId = "session1";

        _approvalService.RequestApprovalAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var service = CreateService(writeTimeout: TimeSpan.FromSeconds(2));
        var writeTask = service.WriteFileAsync("C:\\test.txt", "new content", sessionId);

        var pendingOp = await WaitForPendingOperationAsync(service);
        pendingOp.ShouldNotBeNull();
        pendingOp!.Operation.ShouldBe("write");
        pendingOp.Content.ShouldBe("new content");

        service.SubmitResult(new FileOpResult
        {
            OpId = pendingOp.Id,
            Error = null,
            Content = null,
            Entries = null
        });

        var writeResult = await writeTask;
        writeResult.ShouldBeTrue();

        await _approvalService.Received(1).RequestApprovalAsync(
            sessionId,
            "Write",
            Arg.Is<string>(s => s.Contains("C:\\test.txt")),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteFileAsync_WhenApprovalRejected_ReturnsFalseAndDoesNotQueueOp()
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

        var service = CreateService(writeTimeout: TimeSpan.FromSeconds(2));
        var writeResult = await service.WriteFileAsync("C:\\test.txt", "new content", sessionId);

        writeResult.ShouldBeFalse();
        _pendingFileOps.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadFileAsync_WhenError_ReturnsNull()
    {
        var service = CreateService(readTimeout: TimeSpan.FromSeconds(5));

        var readTask = service.ReadFileAsync("C:\\nonexistent.txt");

        await Task.Delay(100);
        var pending = service.PollPendingOperation();
        pending.ShouldNotBeNull();

        var result = new FileOpResult
        {
            OpId = pending.Id,
            Error = "File not found",
            Content = null,
            Entries = null
        };
        service.SubmitResult(result);

        var readResult = await readTask;
        readResult.ShouldBeNull();
    }

    [Fact]
    public async Task ListDirectoryAsync_WhenTimeout_ReturnsNull()
    {
        var service = CreateService(readTimeout: TimeSpan.FromMilliseconds(100));

        var result = await service.ListDirectoryAsync("C:\\");

        result.ShouldBeNull();
    }


    private static async Task<FileOperation?> WaitForPendingOperationAsync(FileSystemService service, int attempts = 50, int delayMs = 10)
    {
        for (var i = 0; i < attempts; i++)
        {
            var pending = service.PollPendingOperation();
            if (pending != null)
            {
                return pending;
            }
            await Task.Delay(delayMs);
        }

        return null;
    }
}
