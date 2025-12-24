using System.Net;
using System.Net.Sockets;
using System.Text;
using ClaudeWin9xNtServer.Infrastructure;

namespace ClaudeWin9xNtServer.Services;

public class FileTransferService(
    int downloadPort,
    int uploadPort,
    ILogger<FileTransferService> logger,
    string? fileTransferRoot = null,
    long? maxFileSizeBytes = null) : IHostedService
{
    private const long DefaultMaxFileSize = 50 * 1024 * 1024; // 50MB

    private readonly string _fileTransferRoot = fileTransferRoot ?? Directory.GetCurrentDirectory();
    private readonly long _maxFileSize = maxFileSizeBytes ?? DefaultMaxFileSize;
    private TcpListener? _downloadServer;
    private TcpListener? _uploadServer;
    private CancellationTokenSource? _cts;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _downloadServer = new TcpListener(IPAddress.Any, downloadPort);
        _downloadServer.Start();
        logger.LogInformation("File Download server started on TCP port {Port}", downloadPort);

        _uploadServer = new TcpListener(IPAddress.Any, uploadPort);
        _uploadServer.Start();
        logger.LogInformation("File Upload server started on TCP port {Port}", uploadPort);

        _ = RunDownloadServerAsync(_cts.Token);
        _ = RunUploadServerAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _downloadServer?.Stop();
        _uploadServer?.Stop();
        return Task.CompletedTask;
    }

    private static string ReadLine(NetworkStream stream)
    {
        var bytes = new List<byte>();
        int b;
        while ((b = stream.ReadByte()) != -1 && b != '\n')
        {
            if (b != '\r')
            {
                bytes.Add((byte)b);
            }
        }
        return Encoding.ASCII.GetString([.. bytes]);
    }

    private async Task<bool> ValidateApiKeyAsync(NetworkStream stream, TcpClient client, string context)
    {
        var providedKey = ReadLine(stream);
        if (providedKey != IniConfig.ApiKey)
        {
            logger.LogWarning("{Context} auth failed: invalid API key", context);
            await stream.WriteAsync("ERROR Unauthorized\n"u8.ToArray());
            client.Close();
            return false;
        }
        return true;
    }

    private async Task RunDownloadServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _downloadServer!.AcceptTcpClientAsync(cancellationToken);
                _ = HandleDownloadClientAsync(client);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                logger.LogError("Download accept socket error: {ErrorCode} - {Message}", ex.SocketErrorCode, ex.Message);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleDownloadClientAsync(TcpClient client)
    {
        string? filename = null;
        try
        {
            using var stream = client.GetStream();

            if (!await ValidateApiKeyAsync(stream, client, "Download"))
            {
                return;
            }

            filename = ReadLine(stream);

            if (string.IsNullOrEmpty(filename))
            {
                var errMsg = "ERROR Empty filename\n"u8.ToArray();
                await stream.WriteAsync(errMsg);
                client.Close();
                return;
            }

            logger.LogInformation("Download request: {Filename}", filename);

            var fullPath = Path.GetFullPath(Path.Combine(_fileTransferRoot, filename));
            var normalizedRoot = Path.GetFullPath(_fileTransferRoot + Path.DirectorySeparatorChar);
            var tempDir = Path.GetFullPath(Path.GetTempPath());
            var tempPath = Path.GetFullPath(Path.Combine(tempDir, Path.GetFileName(filename)));

            string? actualPath = null;

            if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            {
                actualPath = fullPath;
            }
            else if (tempPath.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase) &&
                     Path.GetDirectoryName(tempPath) == tempDir.TrimEnd(Path.DirectorySeparatorChar) &&
                     File.Exists(tempPath))
            {
                actualPath = tempPath;
                logger.LogDebug("Serving from temp: {TempPath}", tempPath);
            }

            if (actualPath == null)
            {
                logger.LogWarning("Download file not found: {Filename}", filename);
                var errMsg = Encoding.ASCII.GetBytes($"ERROR File not found: {filename}\n");
                await stream.WriteAsync(errMsg);
                client.Close();
                return;
            }

            var fileInfo = new FileInfo(actualPath);
            if (fileInfo.Length > _maxFileSize)
            {
                logger.LogWarning("Download file too large: {Filename} ({Size} bytes, max {MaxSize})",
                    filename, fileInfo.Length, _maxFileSize);
                var errMsg = Encoding.ASCII.GetBytes($"ERROR File too large ({fileInfo.Length} bytes, max {_maxFileSize})\n");
                await stream.WriteAsync(errMsg);
                client.Close();
                return;
            }

            var fileBytes = await File.ReadAllBytesAsync(actualPath);
            var header = Encoding.ASCII.GetBytes($"OK {fileBytes.Length}\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(fileBytes);
            await stream.FlushAsync();

            logger.LogInformation("Download sent {Size} bytes: {Filename}", fileBytes.Length, filename);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Download IO error for {Filename}", filename ?? "unknown");
        }
        catch (SocketException ex)
        {
            logger.LogError("Download socket error for {Filename}: {ErrorCode} - {Message}",
                filename ?? "unknown", ex.SocketErrorCode, ex.Message);
        }
        finally
        {
            client.Close();
        }
    }

    private async Task RunUploadServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _uploadServer!.AcceptTcpClientAsync(cancellationToken);
                _ = HandleUploadClientAsync(client);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                logger.LogError("Upload accept socket error: {ErrorCode} - {Message}", ex.SocketErrorCode, ex.Message);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleUploadClientAsync(TcpClient client)
    {
        string? filename = null;
        try
        {
            using var stream = client.GetStream();

            if (!await ValidateApiKeyAsync(stream, client, "Upload"))
            {
                return;
            }

            filename = ReadLine(stream);

            if (string.IsNullOrEmpty(filename))
            {
                var errMsg = "ERROR Empty filename\n"u8.ToArray();
                await stream.WriteAsync(errMsg);
                client.Close();
                return;
            }

            var sizeLine = ReadLine(stream);

            if (!long.TryParse(sizeLine, out var fileSize))
            {
                logger.LogWarning("Upload invalid size: {SizeLine}", sizeLine);
                var errMsg = Encoding.ASCII.GetBytes($"ERROR Invalid size: {sizeLine}\n");
                await stream.WriteAsync(errMsg);
                client.Close();
                return;
            }

            if (fileSize is < 0 or > int.MaxValue)
            {
                logger.LogWarning("Upload invalid file size: {FileSize}", fileSize);
                var errMsg = "ERROR Invalid file size\n"u8.ToArray();
                await stream.WriteAsync(errMsg);
                client.Close();
                return;
            }

            if (fileSize > _maxFileSize)
            {
                logger.LogWarning("Upload file too large: {Filename} ({Size} bytes, max {MaxSize})",
                    filename, fileSize, _maxFileSize);
                var errMsg = Encoding.ASCII.GetBytes($"ERROR File too large ({fileSize} bytes, max {_maxFileSize})\n");
                await stream.WriteAsync(errMsg);
                client.Close();
                return;
            }

            var fileSizeInt = (int)fileSize;
            logger.LogInformation("Upload receiving: {Filename} ({Size} bytes)", filename, fileSizeInt);

            var fullPath = Path.GetFullPath(Path.Combine(_fileTransferRoot, filename));
            var normalizedRoot = Path.GetFullPath(_fileTransferRoot + Path.DirectorySeparatorChar);

            if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Upload rejected (path escape): {Filename}", filename);
                var errMsg = "ERROR Path not allowed\n"u8.ToArray();
                await stream.WriteAsync(errMsg);
                client.Close();
                return;
            }

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var buffer = new byte[fileSizeInt];
            var totalRead = 0;
            while (totalRead < fileSizeInt)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, fileSizeInt - totalRead));
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead != fileSizeInt)
            {
                logger.LogWarning("Upload incomplete transfer: {Filename} ({Received}/{Expected} bytes)",
                    filename, totalRead, fileSizeInt);
                var errMsg = Encoding.ASCII.GetBytes($"ERROR Incomplete transfer ({totalRead}/{fileSizeInt} bytes)\n");
                await stream.WriteAsync(errMsg);
                client.Close();
                return;
            }

            await File.WriteAllBytesAsync(fullPath, buffer);

            var okMsg = "OK\n"u8.ToArray();
            await stream.WriteAsync(okMsg);

            logger.LogInformation("Upload saved {Size} bytes: {FullPath}", totalRead, fullPath);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Upload IO error for {Filename}", filename ?? "unknown");
        }
        catch (SocketException ex)
        {
            logger.LogError("Upload socket error for {Filename}: {ErrorCode} - {Message}",
                filename ?? "unknown", ex.SocketErrorCode, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Upload access denied for {Filename}", filename ?? "unknown");
        }
        finally
        {
            client.Close();
        }
    }
}
