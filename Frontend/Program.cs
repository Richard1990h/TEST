using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using LittleHelperAI.Dashboard;
using LittleHelperAI.Dashboard.Services;
using Microsoft.JSInterop;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// Load backend API base URL from appsettings.json
string backendApiBaseUrl = builder.Configuration["BackendApi:BaseUrl"] ?? "http://localhost:8001";

// ✅ Add services
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserSessionService>();

// 🔗 SignalR ChatHub service for real-time chat (no HTTP timeout limits)
builder.Services.AddScoped<ChatHubService>();

// ✅ Create HttpClient that automatically adds Authorization header
// ⏱️ TIMEOUT: 5 minutes for project creation (LLM can take a while)
builder.Services.AddScoped(sp =>
{
    var navigationManager = sp.GetRequiredService<NavigationManager>();
    var jsRuntime = sp.GetRequiredService<IJSRuntime>();

    var httpClient = new HttpClient(new AuthenticatedHttpClientHandler(jsRuntime))
    {
        BaseAddress = new Uri(backendApiBaseUrl),
        // ⏱️ Increased timeout for project creation with LLM
        // Default was 100 seconds, now 5 minutes (300 seconds)
        Timeout = TimeSpan.FromMinutes(5)
    };

    return httpClient;
});

// Setup Kestrel binding from appsettings.json
builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.Configure(context.Configuration.GetSection("Kestrel"));
});


builder.Services.AddHttpClient("BackendNoTimeout", client =>
{
    // Calls Backend endpoints and must not time out (local LLM/project build can take minutes)
    client.BaseAddress = new Uri(backendApiBaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan;
});

var app = builder.Build();


// Configure middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // app.UseHsts();
}

// app.UseHttpsRedirection(); // ❌ Disabled (good for now)

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
