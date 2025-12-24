using ClaudeWin9xNtServer.Models.Responses;

namespace ClaudeWin9xNtServer.Services.Interfaces;

public interface IFileSystemService
{
    Task<FileOpResult?> ListDirectoryAsync(string path, CancellationToken cancellationToken = default);
    Task<(string? Content, bool Truncated, int TotalSize)?> ReadFileAsync(string path, int? maxSize = null, CancellationToken cancellationToken = default);
    Task<bool> WriteFileAsync(string path, string content, string? sessionId = null, CancellationToken cancellationToken = default);
    FileOperation? PollPendingOperation();
    void SubmitResult(FileOpResult result);
    (string ZipPath, long Size)? CreateBundle(string sourcePath, string? outputName);
}
