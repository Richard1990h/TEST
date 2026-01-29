using LittleHelperAI.Shared.Models;
using System.Net.Http.Json;

namespace LittleHelperAI.Dashboard.Services
{
    /// <summary>
    /// Centralized service for all AI client operations.
    /// All frontend AI interactions must go through this service.
    /// </summary>
    public class AiClientService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AiClientService> _logger;

        public AiClientService(HttpClient httpClient, ILogger<AiClientService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Send a chat message and get AI response
        /// </summary>
        public async Task<ChatResponse?> SendChatAsync(ChatRequest request)
        {
            try
            {
                _logger.LogInformation("Sending chat request for user {UserId}", request.UserId);

                var response = await _httpClient.PostAsJsonAsync("api/chat/send", request);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<ChatResponse>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error sending chat message");
                return null;
            }
        }

        /// <summary>
        /// Fix code files with structured response
        /// </summary>
        public async Task<List<FixedFileResult>?> FixFilesAsync(List<FixRequestFile> files)
        {
            try
            {
                _logger.LogInformation("Fixing {FileCount} files", files.Count);

                var response = await _httpClient.PostAsJsonAsync("api/files/fix", files);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<List<FixedFileResult>>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error fixing files");
                return null;
            }
        }

        /// <summary>
        /// Get chat history for user
        /// </summary>
        public async Task<List<ChatSummary>?> GetChatHistoryAsync(int userId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/chat/history/{userId}");
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<List<ChatSummary>>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error getting chat history");
                return null;
            }
        }

        /// <summary>
        /// Load a specific chat by ID
        /// </summary>
        public async Task<List<ChatMessageDto>?> LoadChatAsync(int chatId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/chat/load/{chatId}");
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<List<ChatMessageDto>>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error loading chat");
                return null;
            }
        }

        /// <summary>
        /// Get model information
        /// </summary>
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
                _logger.LogError(ex, "Error getting model info");
                return null;
            }
        }

        /// <summary>
        /// Scan a project from uploaded ZIP
        /// </summary>
        public async Task<ProjectScanResult?> ScanProjectAsync(IFormFile file)
        {
            try
            {
                _logger.LogInformation("Scanning project: {FileName}", file.Name);

                using var content = new MultipartFormDataContent();
                using var fileStream = file.OpenReadStream();
                content.Add(new StreamContent(fileStream), "file", file.Name);

                var response = await _httpClient.PostAsync("api/projects/scan", content);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<ProjectScanResult>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error scanning project");
                return null;
            }
        }

        /// <summary>
        /// Generate a new project
        /// </summary>
        public async Task<byte[]?> GenerateProjectAsync(ProjectGenerationRequest request)
        {
            try
            {
                _logger.LogInformation("Generating project: {Description}", request.Description);

                var response = await _httpClient.PostAsJsonAsync("api/projects/generate", request);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error generating project");
                return null;
            }
        }
    }

    // DTOs for file operations
    public class FixRequestFile
    {
        public string Name { get; set; } = "";
        public bool IsFile { get; set; }
        public string Content { get; set; } = "";
    }

    public class FixedFileResult
    {
        public string FileName { get; set; } = "";
        public string FixedCode { get; set; } = "";
        public List<string> IssuesFound { get; set; } = new();
        public List<string> ChangesMade { get; set; } = new();
        public string Explanation { get; set; } = "";
        public bool IsFullyFixed { get; set; }
    }


}
