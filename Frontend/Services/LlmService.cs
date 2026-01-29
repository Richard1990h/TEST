using LittleHelperAI.Shared.Models;
using System.Net.Http.Json;

namespace LittleHelperAI.Dashboard.Services
{
    public class LlmService
    {
        private readonly HttpClient _httpClient;

        public LlmService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }


public async Task<string?> FixSyntaxAsync(string prompt, CancellationToken ct = default)
{
    try
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/llm/fix-syntax",
            new { Prompt = prompt },
            ct
        );

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<FixSyntaxResponse>(cancellationToken: ct);

        return result?.FixedCode;
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"Error fixing syntax: {ex.Message}");
        return null;
    }
}


        public async Task<ModelInfoResponse?> GetModelInfoAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/llm/info");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<ModelInfoResponse>();
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error getting model info: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> LoadModelAsync(string modelName)
        {
            try
            {
                var request = new ModelSelectionRequest { ModelName = modelName };
                var response = await _httpClient.PostAsJsonAsync("api/llm/load", request);
                response.EnsureSuccessStatusCode();
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error loading model: {ex.Message}");
                return false;
            }
        }
    }
}
