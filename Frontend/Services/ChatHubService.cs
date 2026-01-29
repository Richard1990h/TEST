using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace LittleHelperAI.Dashboard.Services;

/// <summary>
/// Service for managing SignalR connection to the chat hub.
/// Provides real-time chat communication without HTTP timeout limitations.
/// </summary>
public class ChatHubService : IAsyncDisposable
{
    private HubConnection? _hubConnection;
    private readonly string _hubUrl;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<ChatHubService> _logger;
    private bool _isConnecting = false;

    // Events for chat communication
    public event Action<string>? OnTokenReceived;
    public event Action<int>? OnChatIdReceived;
    public event Action<dynamic>? OnComplete;
    public event Action<string>? OnError;
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action? OnReconnecting;
    public event Action? OnReconnected;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
    public HubConnectionState State => _hubConnection?.State ?? HubConnectionState.Disconnected;

    public ChatHubService(IConfiguration configuration, IJSRuntime jsRuntime, ILogger<ChatHubService> logger)
    {
        var backendUrl = configuration["BackendApi:BaseUrl"] ?? "http://localhost:8001";
        _hubUrl = $"{backendUrl.TrimEnd('/')}/hubs/chat";
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    /// <summary>
    /// Connect to the SignalR hub with automatic reconnection.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
        {
            _logger.LogInformation("[ChatHubService] Already connected");
            return;
        }

        if (_isConnecting)
        {
            _logger.LogInformation("[ChatHubService] Connection already in progress");
            return;
        }

        _isConnecting = true;

        try
        {
            // Get JWT token for authentication
            var token = await GetJwtTokenAsync();

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options =>
                {
                    if (!string.IsNullOrEmpty(token))
                    {
                        options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                    }
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                .Build();

            // Configure message handlers
            _hubConnection.On<string>("ReceiveToken", (token) =>
            {
                _logger.LogDebug("[ChatHubService] Token received: {TokenLength} chars", token.Length);
                OnTokenReceived?.Invoke(token);
            });

            _hubConnection.On<int>("ReceiveChatId", (chatId) =>
            {
                _logger.LogInformation("[ChatHubService] ChatId received: {ChatId}", chatId);
                OnChatIdReceived?.Invoke(chatId);
            });

            _hubConnection.On<object>("ReceiveComplete", (data) =>
            {
                _logger.LogInformation("[ChatHubService] Completion received");
                OnComplete?.Invoke(data);
            });

            _hubConnection.On<string>("ReceiveError", (error) =>
            {
                _logger.LogWarning("[ChatHubService] Error received: {Error}", error);
                OnError?.Invoke(error);
            });

            // Connection state handlers
            _hubConnection.Closed += async (error) =>
            {
                _logger.LogWarning("[ChatHubService] Connection closed: {Error}", error?.Message ?? "None");
                OnDisconnected?.Invoke();
                await Task.CompletedTask;
            };

            _hubConnection.Reconnecting += (error) =>
            {
                _logger.LogInformation("[ChatHubService] Reconnecting: {Error}", error?.Message ?? "None");
                OnReconnecting?.Invoke();
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += (connectionId) =>
            {
                _logger.LogInformation("[ChatHubService] Reconnected: {ConnectionId}", connectionId);
                OnReconnected?.Invoke();
                return Task.CompletedTask;
            };

            // Start connection
            _logger.LogInformation("[ChatHubService] Connecting to {HubUrl}", _hubUrl);
            await _hubConnection.StartAsync();
            _logger.LogInformation("[ChatHubService] Connected successfully");
            OnConnected?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatHubService] Failed to connect to hub");
            throw;
        }
        finally
        {
            _isConnecting = false;
        }
    }

    /// <summary>
    /// Send a chat message through SignalR (no HTTP timeout).
    /// </summary>
    public async Task SendMessageAsync(object request)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("[ChatHubService] Not connected, attempting to connect...");
            await ConnectAsync();
        }

        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Unable to connect to chat hub");
        }

        _logger.LogInformation("[ChatHubService] Sending message via SignalR");
        await _hubConnection.InvokeAsync("SendMessage", request);
    }

    /// <summary>
    /// Stop the current generation.
    /// </summary>
    public async Task StopGenerationAsync()
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            _logger.LogInformation("[ChatHubService] Stopping generation");
            await _hubConnection.InvokeAsync("StopGeneration");
        }
    }

    /// <summary>
    /// Disconnect from the hub.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_hubConnection != null)
        {
            _logger.LogInformation("[ChatHubService] Disconnecting");
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    private async Task<string?> GetJwtTokenAsync()
    {
        try
        {
            // Note: AuthService saves token as "authToken", not "jwt_token"
            return await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "authToken");
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
