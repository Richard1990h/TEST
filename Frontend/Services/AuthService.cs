using System.Net.Http.Json;
using Microsoft.JSInterop;
using LittleHelperAI.Shared.Models;

namespace LittleHelperAI.Dashboard.Services
{
    public class AuthService
    {
        private readonly HttpClient _http;
        private readonly IJSRuntime _js;

        // Expose HttpClient for referral validation
        public HttpClient Http => _http;

        public AuthService(HttpClient http, IJSRuntime js)
        {
            _http = http;
            _js = js;
        }

        public async Task<LoginResponse?> Login(LoginRequest request)
        {
            var response = await _http.PostAsJsonAsync("api/auth/login", request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return new LoginResponse { ErrorMessage = error };
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResultDto>();

            if (result != null)
            {
                // Save token to localStorage
                await _js.InvokeVoidAsync("localStorage.setItem", "authToken", result.Token);

                return new LoginResponse
                {
                    User = result.User,
                    ErrorMessage = null
                };
            }

            return new LoginResponse { ErrorMessage = "Unknown error." };
        }

        public async Task<string?> Register(RegisterRequest request)
        {
            var response = await _http.PostAsJsonAsync("api/auth/register", request);

            if (!response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            return null;
        }
    }

    public class LoginResponse
    {
        public UserDto? User { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class LoginResultDto
    {
        public string Token { get; set; } = string.Empty;
        public DateTime Expires { get; set; }
        public UserDto User { get; set; } = new();
    }
}
