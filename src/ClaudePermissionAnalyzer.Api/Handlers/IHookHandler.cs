using ClaudePermissionAnalyzer.Api.Models;

namespace ClaudePermissionAnalyzer.Api.Handlers;

public interface IHookHandler
{
    Task<HookOutput> HandleAsync(HookInput input, HandlerConfig config, string sessionContext, CancellationToken cancellationToken = default);
}
