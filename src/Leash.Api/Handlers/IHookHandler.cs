using Leash.Api.Models;

namespace Leash.Api.Handlers;

public interface IHookHandler
{
    Task<HookOutput> HandleAsync(HookInput input, HandlerConfig config, string sessionContext, CancellationToken cancellationToken = default);
}
