using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RestronautService.Middlewares;

public class AuthHeaderHandler : DelegatingHandler
{
    private readonly IAuthTokenStore authTokenStore;

    public AuthHeaderHandler(IAuthTokenStore authTokenStore)
    {
        this.authTokenStore = authTokenStore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await authTokenStore.GetAuthTokenAsync();
        request.Headers.Add("x-api-key", token);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}