using System.Text.Json;
using System.Threading.Tasks;
using LittleHelperAI.Shared.Models;
using Microsoft.JSInterop;
using System.Net.Http;
using System.Net.Http.Json;

public class UserSessionService
{
    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private UserDto? _user;
    private bool _isInitializing = false;

    private const string StorageKey = "loggedInUser";
    private const string AuthTokenKey = "authToken";

    public bool IsInitialized { get; private set; } = false;

    /// <summary>
    /// True while async initialization is in progress (fetching fresh data)
    /// </summary>
    public bool IsHydrating => _isInitializing;

    // =====================================================
    // 🔔 CREDITS CHANGED EVENT (for immediate UI updates)
    // =====================================================
    public event Action? OnCreditsChanged;

    // =====================================================
    // 🔔 PLAN CHANGED EVENT (for immediate UI updates)
    // =====================================================
    public event Action? OnPlanChanged;

    // =====================================================
    // 🔔 NOTIFICATIONS CHANGED EVENT (NEW – matches existing pattern)
    // =====================================================
    public event Action? OnNotificationsChanged;

    // =====================================================
    // 🔔 USER STATE CHANGED EVENT (fires when user data is refreshed)
    // =====================================================
    public event Action? OnUserStateChanged;

    // 🔹 UPDATED: HttpClient injected
    public UserSessionService(IJSRuntime js, HttpClient http)
    {
        _js = js;
        _http = http;
    }

    // =====================================================
    // 🔁 RESTORE SESSION ON APP INIT
    // =====================================================
    public async Task InitializeAsync()
    {
        if (IsInitialized)
            return;

        await _stateLock.WaitAsync();
        try
        {
            if (IsInitialized) return; // Double-check after lock

            var userJson = await _js.InvokeAsync<string>(
                "localStorage.getItem",
                StorageKey
            );

            if (!string.IsNullOrWhiteSpace(userJson))
            {
                _user = JsonSerializer.Deserialize<UserDto>(userJson);

                // 🔥 Reattach AI runtime + refresh credits from DB
                // This is now AWAITED properly to prevent race conditions
                _isInitializing = true;
                try
                {
                    await InitializeAIAsync();
                }
                finally
                {
                    _isInitializing = false;
                }
            }

            IsInitialized = true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    // =====================================================
    // ✅ LOGIN SUCCESS
    // =====================================================
    public async Task SetUserAsync(UserDto user)
    {
        await _stateLock.WaitAsync();
        try
        {
            _user = user;

            var json = JsonSerializer.Serialize(user);
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);

            // 🔥 Login must ALWAYS reinitialise AI + credits
            // Now properly awaited with state tracking
            _isInitializing = true;
            try
            {
                await InitializeAIAsync();
            }
            finally
            {
                _isInitializing = false;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    // =====================================================
    // 🚪 LOGOUT
    // =====================================================
    public async Task LogoutAsync()
    {
        _user = null;
        IsInitialized = false;

        await _js.InvokeVoidAsync("localStorage.removeItem", AuthTokenKey);
        await _js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
    }

    // =====================================================
    // 🤖 AI + CREDIT REATTACH
    // =====================================================
    private async Task InitializeAIAsync()
    {
        try
        {
            await _http.PostAsync("/api/llm/reinit", null);

            var freshUser = await _http.GetFromJsonAsync<UserDto>(
                "/api/users/me"
            );

            if (freshUser != null)
            {
                var creditsChanged = _user?.Credits != freshUser.Credits;

                _user = freshUser;

                var json = JsonSerializer.Serialize(freshUser);
                await _js.InvokeVoidAsync(
                    "localStorage.setItem",
                    StorageKey,
                    json
                );

                // 🔔 Notify UI that user state has been refreshed
                OnUserStateChanged?.Invoke();

                // Fire credits changed event if credits value changed
                if (creditsChanged)
                    OnCreditsChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            // Log error but don't break login / UI
            Console.WriteLine($"[UserSessionService] InitializeAIAsync error: {ex.Message}");
        }
    }

    // =====================================================
    // 💳 UPDATE CREDITS AT RUNTIME
    // =====================================================
    public async Task UpdateCreditsAsync(double credits)
    {
        if (_user == null)
            return;

        await _stateLock.WaitAsync();
        try
        {
            if (_user == null) return;

            _user.Credits = credits;

            var json = JsonSerializer.Serialize(_user);
            await _js.InvokeVoidAsync(
                "localStorage.setItem",
                StorageKey,
                json
            );
        }
        finally
        {
            _stateLock.Release();
        }

        // Fire event outside lock to prevent deadlocks
        OnCreditsChanged?.Invoke();
    }

    // =====================================================
    // 📋 NOTIFY PLAN CHANGED
    // =====================================================
    public void NotifyPlanChanged()
    {
        OnPlanChanged?.Invoke();
    }

    // =====================================================
    // 🔔 NOTIFY NOTIFICATIONS CHANGED (NEW)
    // =====================================================
    public void NotifyNotificationsChanged()
    {
        OnNotificationsChanged?.Invoke();
    }

    // =====================================================
    // 🔄 REFRESH USER DATA
    // =====================================================
    public async Task RefreshUserAsync()
    {
        if (_user == null || _user.Id <= 0)
            return;

        // Don't refresh if already initializing (prevents race condition)
        if (_isInitializing)
            return;

        try
        {
            var freshUser = await _http.GetFromJsonAsync<UserDto>("/api/users/me");

            if (freshUser != null)
            {
                bool creditsChanged;

                await _stateLock.WaitAsync();
                try
                {
                    creditsChanged = _user?.Credits != freshUser.Credits;

                    _user = freshUser;

                    var json = JsonSerializer.Serialize(freshUser);
                    await _js.InvokeVoidAsync(
                        "localStorage.setItem",
                        StorageKey,
                        json
                    );
                }
                finally
                {
                    _stateLock.Release();
                }

                // Fire events outside lock
                OnUserStateChanged?.Invoke();
                if (creditsChanged)
                    OnCreditsChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UserSessionService] RefreshUserAsync error: {ex.Message}");
        }
    }

    // =====================================================
    // ACCESSORS
    // =====================================================
    public UserDto? GetUser() => _user;
    public bool IsLoggedIn() => _user != null;
}
