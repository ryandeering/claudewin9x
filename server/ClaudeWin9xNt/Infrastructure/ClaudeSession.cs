using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ClaudeWin9xNtServer.Infrastructure;

public class ClaudeSession(
    string sessionId,
    string workingDirectory,
    string windowsVersion,
    ILogger logger) : IDisposable
{
    private const int MaxBufferSize = 1024 * 1024;

    private Process? _process;
    private readonly string _workingDirectory = workingDirectory;
    private readonly string _sessionId = sessionId;
    private readonly string _windowsVersion = windowsVersion;
    private readonly StringBuilder _outputBuffer = new();
    private readonly StringBuilder _parsedOutputBuffer = new();
    private readonly ClaudeOutputParser _parser = new();
    private readonly object _lock = new();

    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    public bool IsRunning => _process is { HasExited: false };

    public string SessionId => _sessionId;

    private string GetSystemPrompt() => SystemPromptTemplate.Generate(_windowsVersion, _sessionId);

    private static string BuildPath()
    {
        var systemPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var nodeDir = Environment.GetEnvironmentVariable("CLAUDE_NODE_DIR");

        if (string.IsNullOrEmpty(nodeDir))
        {
            return systemPath;
        }

        var separator = OperatingSystem.IsWindows() ? ";" : ":";
        return $"{nodeDir}{separator}{systemPath}";
    }

    private static (string FileName, string? ScriptPath) FindClaudeCli()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var nativePaths = new List<string>
        {
            Path.Combine(home, ".claude", "bin", "claude"),
            Path.Combine(home, ".local", "bin", "claude"),
        };

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            nativePaths.Add(Path.Combine(localAppData, "Claude", "claude.exe"));
        }

        foreach (var path in nativePaths)
        {
            if (File.Exists(path))
            {
                return (path, null);
            }
        }

        var npmPaths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            npmPaths.Add(Path.Combine(appData, "npm", "node_modules", "@anthropic-ai", "claude-code", "cli.js"));
        }
        else
        {
            npmPaths.Add(Path.Combine(home, ".npm-global", "lib", "node_modules", "@anthropic-ai", "claude-code", "cli.js"));
            npmPaths.Add("/usr/local/lib/node_modules/@anthropic-ai/claude-code/cli.js");
            npmPaths.Add("/usr/lib/node_modules/@anthropic-ai/claude-code/cli.js");
        }

        foreach (var path in npmPaths)
        {
            if (File.Exists(path))
            {
                return ("node", path);
            }
        }

        return ("claude", null);
    }


    public void UpdateHeartbeat() => LastActivity = DateTime.UtcNow;

    public void Start()
    {
        var escapedPrompt = GetSystemPrompt().Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");

        var envCliPath = Environment.GetEnvironmentVariable("CLAUDE_CLI_PATH");
        var (fileName, scriptPath) = envCliPath != null
            ? (envCliPath.EndsWith(".js") ? "node" : envCliPath, envCliPath.EndsWith(".js") ? envCliPath : null)
            : FindClaudeCli();

        var cliArgs = "--input-format stream-json --output-format stream-json --print --verbose --dangerously-skip-permissions --append-system-prompt";
        var arguments = scriptPath != null
            ? $"\"{scriptPath}\" {cliArgs} \"{escapedPrompt}\""
            : $"{cliArgs} \"{escapedPrompt}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Environment =
            {
                ["TERM"] = "dumb",
                ["NO_COLOR"] = "1",
                ["PATH"] = BuildPath()
            }
        };

        _process = new Process { StartInfo = startInfo };

        _process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                logger.LogDebug("Claude stdout: {Data}", e.Data);
                lock (_lock)
                {
                    if (_outputBuffer.Length < MaxBufferSize)
                    {
                        _outputBuffer.AppendLine(e.Data);
                    }
                    ParseJsonLine(e.Data);
                }
            }
        };

        _process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                logger.LogWarning("Claude stderr: {Data}", e.Data);
                lock (_lock)
                {
                    if (_parsedOutputBuffer.Length < MaxBufferSize)
                    {
                        _parsedOutputBuffer.AppendLine($"[ERR] {e.Data}");
                    }
                }
            }
        };

        _process.Start();
        logger.LogInformation("Claude process started with PID {Pid}", _process.Id);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    private void AppendParsedOutput(string? text)
    {
        if (text != null && _parsedOutputBuffer.Length < MaxBufferSize)
        {
            _parsedOutputBuffer.Append(text);
        }
    }

    private void AppendParsedOutputLine(string? text = null)
    {
        if (_parsedOutputBuffer.Length < MaxBufferSize)
        {
            if (text != null)
            {
                _parsedOutputBuffer.AppendLine(text);
            }
            else
            {
                _parsedOutputBuffer.AppendLine();
            }
        }
    }

    private void ParseJsonLine(string line)
    {
        var result = _parser.Parse(line);

        if (result.Text != null)
        {
            if (result.AppendNewline)
            {
                AppendParsedOutputLine(result.Text);
            }
            else
            {
                AppendParsedOutput(result.Text);
            }
        }
        else if (result.AppendNewline)
        {
            AppendParsedOutputLine();
        }
    }

    public async Task SendInput(string text)
    {
        if (_process?.StandardInput != null && IsRunning)
        {
            var escapedContent = JsonSerializer.Serialize(text, AppJsonSerializerContext.Default.String);
            var json = $"{{\"type\":\"user\",\"message\":{{\"role\":\"user\",\"content\":{escapedContent}}}}}\n";
            await _process.StandardInput.WriteAsync(json);
            await _process.StandardInput.FlushAsync();
        }
    }

    public string GetOutput()
    {
        lock (_lock)
        {
            var output = _outputBuffer.ToString();
            _outputBuffer.Clear();
            return output;
        }
    }

    public string GetParsedOutput()
    {
        lock (_lock)
        {
            var output = _parsedOutputBuffer.ToString();
            _parsedOutputBuffer.Clear();
            return output;
        }
    }

    public void Stop()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error stopping Claude process");
            }
        }
        _process?.Dispose();
        _process = null;
    }

    public void Dispose() => Stop();
}
