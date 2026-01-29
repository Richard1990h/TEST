using Microsoft.JSInterop;
using System.Net.Http;
using System.Net.Http.Headers;

namespace LittleHelperAI.Dashboard.Services
{
    public class AuthenticatedHttpClientHandler : DelegatingHandler
    {
        private readonly IJSRuntime _jsRuntime;

        public AuthenticatedHttpClientHandler(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
            InnerHandler = new HttpClientHandler(); // Important!
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "authToken");

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
