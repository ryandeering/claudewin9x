using System.Collections.Concurrent;
using System.IO.Compression;
using ClaudeWin9xNtServer.Infrastructure;
using ClaudeWin9xNtServer.Models.Responses;
using ClaudeWin9xNtServer.Services.Interfaces;

namespace ClaudeWin9xNtServer.Services;

public class FileSystemService(
    ConcurrentDictionary<string, FileOperation> pendingFileOps,
    ConcurrentDictionary<string, TaskCompletionSource<FileOpResult>> fileOpWaiters,
    IApprovalService approvalService,
    ILogger<FileSystemService> logger,
    TimeSpan? readTimeout = null,
    TimeSpan? writeTimeout = null) : IFileSystemService
{
    private readonly TimeSpan _readTimeout = readTimeout ?? TimeSpan.FromSeconds(120);
    private readonly TimeSpan _writeTimeout = writeTimeout ?? TimeSpan.FromSeconds(60);

    private async Task<FileOpResult?> QueueOperationAsync(FileOperation op, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<FileOpResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!fileOpWaiters.TryAdd(op.Id, tcs))
        {
            logger.LogError("Failed to register waiter for operation {OpId}", op.Id);
            return null;
        }

        if (!pendingFileOps.TryAdd(op.Id, op))
        {
            fileOpWaiters.TryRemove(op.Id, out _);
            logger.LogError("Failed to queue operation {OpId} (duplicate)", op.Id);
            return null;
        }

        logger.LogInformation("Queued {Operation} {OpId}: {Path}", op.Operation, op.Id, op.Path);

        try
        {
            return await tcs.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Timeout waiting for {Operation} operation {OpId}", op.Operation, op.Id);
            return null;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Operation cancelled {OpId}", op.Id);
            return null;
        }
        finally
        {
            fileOpWaiters.TryRemove(op.Id, out _);
            pendingFileOps.TryRemove(op.Id, out _);
        }
    }

    public Task<FileOpResult?> ListDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        var op = new FileOperation
        {
            Id = IdGenerator.NewId(),
            Operation = "list",
            Path = path,
            Content = null,
            Status = "pending"
        };
        return QueueOperationAsync(op, _readTimeout, cancellationToken);
    }

    public async Task<(string? Content, bool Truncated, int TotalSize)?> ReadFileAsync(string path, int? maxSize = null, CancellationToken cancellationToken = default)
    {
        var op = new FileOperation
        {
            Id = IdGenerator.NewId(),
            Operation = "read",
            Path = path,
            Content = null,
            Status = "pending"
        };

        var result = await QueueOperationAsync(op, _readTimeout, cancellationToken);
        if (result == null || result.Error != null)
        {
            return null;
        }

        var content = result.Content ?? "";
        var limit = maxSize ?? 50000;

        return content.Length > limit
            ? (content[..limit], true, content.Length)
            : (content, false, content.Length);
    }

    public async Task<bool> WriteFileAsync(string path, string content, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        if (sessionId != null)
        {
            var description = $"Write {content.Length} bytes to {path}";
            var approved = await approvalService.RequestApprovalAsync(
                sessionId,
                "Write",
                description,
                _writeTimeout,
                cancellationToken);

            if (!approved)
            {
                logger.LogWarning("Write rejected by user: {Path}", path);
                return false;
            }
        }

        var op = new FileOperation
        {
            Id = IdGenerator.NewId(),
            Operation = "write",
            Path = path,
            Content = content,
            Status = "pending"
        };

        var result = await QueueOperationAsync(op, _writeTimeout, cancellationToken);
        return result?.Error == null;
    }

    public FileOperation? PollPendingOperation()
    {
        var pending = pendingFileOps.Values
            .Where(op => op.Status == "pending")
            .OrderBy(op => op.Id)
            .FirstOrDefault();

        if (pending == null)
        {
            return null;
        }

        var dispatched = pending with { Status = "dispatched" };
        if (!pendingFileOps.TryUpdate(pending.Id, dispatched, pending))
        {
            logger.LogWarning("Failed to dispatch file operation {OpId} (concurrent modification)", pending.Id);
            return null;
        }

        logger.LogInformation("Dispatched {OpId} to client: {Operation} {Path}", pending.Id, pending.Operation, pending.Path);
        return dispatched;
    }

    public void SubmitResult(FileOpResult result)
    {
        if (string.IsNullOrEmpty(result.OpId))
        {
            return;
        }

        logger.LogInformation("Result received for {OpId}: error={Error}", result.OpId, result.Error ?? "none");

        if (fileOpWaiters.TryRemove(result.OpId, out var tcs))
        {
            tcs.TrySetResult(result);
        }

        pendingFileOps.TryRemove(result.OpId, out _);
    }

    public (string ZipPath, long Size)? CreateBundle(string sourcePath, string? outputName)
    {
        var sanitizedName = outputName ?? "bundle.zip";
        sanitizedName = Path.GetFileName(sanitizedName);

        if (string.IsNullOrEmpty(sanitizedName) || sanitizedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            sanitizedName = "bundle.zip";
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var currentDir = Path.GetFullPath(Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar);

        if (!fullSourcePath.StartsWith(currentDir, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Bundle rejected (path escape): {SourcePath}", sourcePath);
            return null;
        }

        if (!Directory.Exists(fullSourcePath))
        {
            return null;
        }

        var outputPath = Path.Combine(Path.GetTempPath(), sanitizedName);

        try
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            ZipFile.CreateFromDirectory(fullSourcePath, outputPath, CompressionLevel.Fastest, false);
            var fileInfo = new FileInfo(outputPath);

            logger.LogInformation("Created bundle {OutputPath} ({Size} bytes) from {SourcePath}",
                outputPath, fileInfo.Length, fullSourcePath);

            return (outputPath, fileInfo.Length);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "IO error creating bundle {OutputPath}", outputPath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied for bundle source {SourcePath}", fullSourcePath);
            return null;
        }
    }
}
