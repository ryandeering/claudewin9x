using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ClaudeWin9xNtServer.Endpoints;
using ClaudeWin9xNtServer.Infrastructure;
using ClaudeWin9xNtServer.Models.Requests;
using ClaudeWin9xNtServer.Models.Responses;
using ClaudeWin9xNtServer.Services;
using ClaudeWin9xNtServer.Services.Interfaces;

IniConfig.Load();

var builder = WebApplication.CreateSlimBuilder(args);

builder.WebHost.UseUrls($"http://0.0.0.0:{IniConfig.ApiPort}");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var pendingCommands = new ConcurrentDictionary<string, CommandRequest>();
var commandResults = new ConcurrentDictionary<string, CommandResult>();
var commandWaiters = new ConcurrentDictionary<string, TaskCompletionSource<CommandResult>>();
var pendingFileOps = new ConcurrentDictionary<string, FileOperation>();
var fileOpWaiters = new ConcurrentDictionary<string, TaskCompletionSource<FileOpResult>>();
var pendingApprovals = new ConcurrentDictionary<string, ToolApprovalRequest>();
var approvalWaiters = new ConcurrentDictionary<string, TaskCompletionSource<bool>>();

builder.Services.AddSingleton(pendingCommands);
builder.Services.AddSingleton(commandResults);
builder.Services.AddSingleton(commandWaiters);
builder.Services.AddSingleton(pendingFileOps);
builder.Services.AddSingleton(fileOpWaiters);
builder.Services.AddSingleton(pendingApprovals);
builder.Services.AddSingleton(approvalWaiters);

builder.Services.AddSingleton(sp => new SessionService(
    sp.GetRequiredService<ILogger<SessionService>>()
));
builder.Services.AddSingleton<ISessionService>(sp => sp.GetRequiredService<SessionService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionService>());

builder.Services.AddSingleton<IApprovalService>(sp => new ApprovalService(
    sp.GetRequiredService<ConcurrentDictionary<string, ToolApprovalRequest>>(),
    sp.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<bool>>>(),
    sp.GetRequiredService<ILogger<ApprovalService>>()
));

builder.Services.AddSingleton<ICommandService>(sp => new CommandService(
    sp.GetRequiredService<ConcurrentDictionary<string, CommandRequest>>(),
    sp.GetRequiredService<ConcurrentDictionary<string, CommandResult>>(),
    sp.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<CommandResult>>>(),
    sp.GetRequiredService<IApprovalService>(),
    sp.GetRequiredService<ILogger<CommandService>>()
));

builder.Services.AddSingleton<IFileSystemService>(sp => new FileSystemService(
    sp.GetRequiredService<ConcurrentDictionary<string, FileOperation>>(),
    sp.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<FileOpResult>>>(),
    sp.GetRequiredService<IApprovalService>(),
    sp.GetRequiredService<ILogger<FileSystemService>>()
));

builder.Services.AddSingleton(sp => new FileTransferService(
    IniConfig.DownloadPort,
    IniConfig.UploadPort,
    sp.GetRequiredService<ILogger<FileTransferService>>(),
    Path.GetTempPath()
));
builder.Services.AddHostedService(sp => sp.GetRequiredService<FileTransferService>());

var app = builder.Build();

// Disable chunked transfer encoding for compatibility
app.Use(async (context, next) =>
{
    context.Response.Headers["Transfer-Encoding"] = "";
    await next();
});

app.Use(async (context, next) =>
{
    var providedKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
    if (providedKey != IniConfig.ApiKey)
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
        return;
    }
    await next();
});

[UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Minimal APIs are expected to use reflection")]
[UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling", Justification = "Minimal APIs require runtime code generation")]
static void ConfigureEndpoints(WebApplication app) => app.MapProxyEndpoints();

ConfigureEndpoints(app);

Console.WriteLine("===========================================");
Console.WriteLine("  ClaudeWin9xNt Server");
Console.WriteLine($"  Listening on http://0.0.0.0:{IniConfig.ApiPort}");
Console.WriteLine("===========================================");
Console.WriteLine();

Console.WriteLine("Config:");
Console.WriteLine($"  api_port:         {IniConfig.ApiPort}");
Console.WriteLine($"  download_port:    {IniConfig.DownloadPort}");
Console.WriteLine($"  upload_port:      {IniConfig.UploadPort}");
Console.WriteLine($"  temp_dir:         {Path.GetTempPath()}");
Console.WriteLine();

Console.WriteLine("Note: Claude CLI runs with --dangerously-skip-permissions.");
Console.WriteLine("      Commands to client are still gated for approval.");
Console.WriteLine();

Console.WriteLine("Endpoints:");
Console.WriteLine("  Claude Code: /start, /input, /output, /stop, /sessions, /heartbeat");
Console.WriteLine("  Commands:    /cmd/queue, /cmd/poll, /cmd/result, /cmd/status");
Console.WriteLine("  Filesystem:  /fs/list, /fs/read, /fs/write, /fs/poll, /fs/result");
Console.WriteLine("  Approvals:   /approval/poll, /approval/respond");
Console.WriteLine();

app.Run();
