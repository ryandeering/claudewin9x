using ClaudeWin9xNtServer.Models.Requests;
using ClaudeWin9xNtServer.Models.Responses;

namespace ClaudeWin9xNtServer.Services.Interfaces;

public interface ICommandService
{
    Task<CommandResult?> QueueCommandAsync(string command, string? workingDirectory, string? sessionId = null, CancellationToken cancellationToken = default);
    CommandRequest? PollPendingCommand();
    void SubmitResult(CommandResult result);
    CommandResult? GetCommandStatus(string commandId);
    bool IsPending(string commandId);
    string? GetPendingStatus(string commandId);
}
