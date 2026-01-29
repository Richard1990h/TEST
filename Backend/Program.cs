using LittleHelperAI.Data;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Backend.Logging;
using LittleHelperAI.Backend.Services;
using LittleHelperAI.Backend.Services.Pipeline;
using LittleHelperAI.Backend.Infrastructure;
using LittleHelperAI.Backend.Infrastructure.RateLimiting;
using LittleHelperAI.Shared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using LittleHelperAI.Backend.Hubs;
using LittleHelperAI.KingFactory;
using LLama.Native;

// CRITICAL: Initialize LLamaSharp native library BEFORE any other LLamaSharp types are accessed
// This must happen at the very start of the program to avoid AccessViolationException
try
{
    // Check if CUDA is likely available by looking for nvidia-smi
    var cudaAvailable = false;
    try
    {
        var nvidiaSmi = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrEmpty(nvidiaSmi) || File.Exists(@"C:\Windows\System32\nvidia-smi.exe"))
        {
            cudaAvailable = true;
        }
    }
    catch { }

    if (cudaAvailable)
    {
        // Add CUDA library path to PATH environment variable
        var cudaPath = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "cuda12");
        if (Directory.Exists(cudaPath))
        {
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!currentPath.Contains(cudaPath, StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("PATH", cudaPath + ";" + currentPath);
            }
        }
        NativeLibraryConfig.All.WithCuda();
        Console.WriteLine("LLamaSharp: CUDA backend configured");
    }
    else
    {
        Console.WriteLine("LLamaSharp: Using CPU backend (CUDA not detected)");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"LLamaSharp: Native library configuration failed: {ex.Message}");
}

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("PipelineTrace", LogLevel.Trace);

var logFilePath = builder.Configuration["Logging:File:Path"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "logs", "ai-debug.log");
builder.Logging.AddProvider(new FileLoggerProvider(logFilePath));

#region Kestrel Configuration

builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.Configure(context.Configuration.GetSection("Kestrel"));
});

#endregion

#region CORS

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorFrontend", policy =>
    {
        var origins =
            builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:50792" };

        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

#endregion

#region Authentication (JWT)

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey = builder.Configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

#endregion

#region Core MVC + Swagger

builder.Services.AddControllers()
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.MaxDepth = 128;
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 52428800; // 50MB
    options.Limits.MaxRequestBufferSize = 52428800;
    options.Limits.MaxRequestLineSize = 16384;
});

builder.Services.AddScoped<KnowledgeStoreService>();
builder.Services.AddScoped<CreditPolicyService>();
builder.Services.AddSingleton<CreditSettings>();
builder.Services.AddScoped<LittleHelperAI.Backend.Services.Notifications.NotificationStore>();
builder.Services.AddScoped<LittleHelperAI.Backend.Services.Notifications.NotificationService>();
builder.Services.AddScoped<AdminAuditLogger>();
builder.Services.AddScoped<LittleHelperAI.Backend.Services.Admin.AdminAuditStore>();
builder.Services.AddScoped<SnapshotService>();
builder.Services.AddHostedService<SnapshotHostedService>();

builder.Services.AddScoped<LittleHelperAI.Backend.Services.ICreditSecurityService, LittleHelperAI.Backend.Services.CreditSecurityService>();
builder.Services.AddScoped<LittleHelperAI.Backend.Services.IPurchaseRewardService, LittleHelperAI.Backend.Services.PurchaseRewardService>();

builder.Services.AddHostedService<LittleHelperAI.Backend.Services.SubscriptionExpiryHostedService>();

#endregion

#region Pipeline Infrastructure Services

// Configuration bindings
builder.Services.Configure<RequestTimeoutOptions>(
    builder.Configuration.GetSection(RequestTimeoutOptions.SectionName));
builder.Services.Configure<CircuitBreakerOptions>(
    builder.Configuration.GetSection(CircuitBreakerOptions.SectionName));
builder.Services.Configure<RetryPolicyOptions>(
    builder.Configuration.GetSection(RetryPolicyOptions.SectionName));
builder.Services.Configure<RateLimitingOptions>(
    builder.Configuration.GetSection(RateLimitingOptions.SectionName));
builder.Services.Configure<DeadLetterQueueOptions>(
    builder.Configuration.GetSection(DeadLetterQueueOptions.SectionName));

// Infrastructure services
builder.Services.AddSingleton<IRequestTimeoutService, RequestTimeoutService>();
builder.Services.AddSingleton<ILlmCircuitBreaker, LlmCircuitBreaker>();
builder.Services.AddSingleton<ILlmFailureClassifier, LlmFailureClassifier>();
builder.Services.AddSingleton<ILlmRetryPolicy, LlmRetryPolicy>();
builder.Services.AddSingleton<IUserRateLimiter, UserRateLimiter>();

// Dead letter queue
builder.Services.AddSingleton<IDeadLetterQueueService, DeadLetterQueueService>();
builder.Services.AddHostedService<DeadLetterWriterHostedService>();

// Metrics
builder.Services.AddSingleton<IPipelineMetrics>(sp =>
    new PipelineMetrics(
        sp.GetService<ILlmCircuitBreaker>(),
        sp.GetService<IDeadLetterQueueService>()));

#endregion

#region Swagger and API Explorer

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSignalR(options =>
{
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(30);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    options.MaximumReceiveMessageSize = 52428800;
});

#endregion

#region Database (MySQL)

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var conn = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseMySql(conn, ServerVersion.AutoDetect(conn));
});

// Add DbContextFactory for services that need scoped contexts (Pipeline V2 stores)
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
{
    var conn = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseMySql(conn, ServerVersion.AutoDetect(conn));
});

#endregion

#region HTTP Clients + External Services

builder.Services.AddHttpClient();

// Ollama Service for external LLM provider
builder.Services.AddHttpClient<LittleHelperAI.KingFactory.Engine.IOllamaService, LittleHelperAI.KingFactory.Engine.OllamaService>();

#endregion

#region Payments

builder.Services.AddScoped<StripeService>();

#endregion

#region AI Factory

// Configure sandboxed project directory
var projectsDirectory = builder.Configuration["Factory:ProjectsDirectory"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LittleHelperAI", "Projects");

// Ensure the projects directory exists
if (!Directory.Exists(projectsDirectory))
{
    Directory.CreateDirectory(projectsDirectory);
    Console.WriteLine($"[Startup] Created projects directory: {projectsDirectory}");
}

builder.Services.AddFactory(config =>
{
    var modelPath = builder.Configuration["Factory:ModelPath"];
    var modelDirectory = builder.Configuration["Factory:ModelDirectory"];
    var modelFile = builder.Configuration["Factory:ModelFile"];

    if (!string.IsNullOrWhiteSpace(modelPath))
    {
        config.LlmConfig.ModelPath = modelPath;
    }

    var contentRoot = builder.Environment.ContentRootPath;
    var baseLlmDir = Path.Combine(AppContext.BaseDirectory, "LLM");

    var resolvedLlmDir = FindLlmDirectoryWithModels(contentRoot)
        ?? FindLlmDirectoryWithModels(AppContext.BaseDirectory)
        ?? FindLlmDirectoryWithModelsDeep(contentRoot)
        ?? FindLlmDirectoryWithModelsDeep(AppContext.BaseDirectory)
        ?? (Directory.Exists(baseLlmDir) ? baseLlmDir : null)
        ?? (!string.IsNullOrWhiteSpace(modelDirectory) ? modelDirectory : null);

    if (!string.IsNullOrWhiteSpace(resolvedLlmDir))
    {
        config.LlmConfig.ModelDirectory = resolvedLlmDir;
    }

    if (!string.IsNullOrWhiteSpace(modelFile))
    {
        config.LlmConfig.ModelFile = modelFile;
    }

    static string? FindLlmDirectoryWithModels(string startDir)
    {
        var current = new DirectoryInfo(startDir);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "LLM");
            if (Directory.Exists(candidate) && HasGguf(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }

        return null;
    }

    static bool HasGguf(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.gguf", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    static string? FindLlmDirectoryWithModelsDeep(string startDir)
    {
        try
        {
            var candidates = Directory.EnumerateDirectories(startDir, "LLM", SearchOption.AllDirectories)
                .Where(HasGguf)
                .Select(dir =>
                {
                    var newest = Directory.EnumerateFiles(dir, "*.gguf", SearchOption.AllDirectories)
                        .Select(f => new FileInfo(f).LastWriteTimeUtc)
                        .DefaultIfEmpty(DateTime.MinValue)
                        .Max();
                    return (dir, newest);
                })
                .OrderByDescending(x => x.newest)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(candidates.dir) ? null : candidates.dir;
        }
        catch
        {
            return null;
        }
    }

    // GPU configuration for CUDA
    config.LlmConfig.GpuLayerCount = builder.Configuration.GetValue<int>("Factory:GpuLayers", -1); // -1 = all layers on GPU
    config.LlmConfig.ContextSize = builder.Configuration.GetValue<uint>("Factory:ContextSize", 4096);
    config.LlmConfig.BatchSize = builder.Configuration.GetValue<uint>("Factory:BatchSize", 0);
    config.LlmConfig.Threads = builder.Configuration.GetValue<uint>("Factory:Threads", 0);

    // Pipeline V2 feature flag
    config.LlmConfig.UsePipelineV2 = builder.Configuration.GetValue<bool>("Factory:UsePipelineV2", false);

    // Filesystem security - restrict to sandboxed projects directory
    config.FilesystemConfig.BaseDirectory = projectsDirectory;

    // Shell security - set working directory
    config.ShellConfig.WorkingDirectory = config.FilesystemConfig.BaseDirectory;
});

// Override the null file event notifier with SignalR implementation
builder.Services.AddSingleton<LittleHelperAI.KingFactory.Tools.Filesystem.IFileEventNotifier, LittleHelperAI.Backend.Services.SignalRFileEventNotifier>();

// Pipeline V2 stores and execution service
builder.Services.AddSingleton<LittleHelperAI.KingFactory.Pipeline.Storage.IPipelineStore, LittleHelperAI.Backend.Services.Pipeline.Storage.DatabasePipelineStore>();
builder.Services.AddSingleton<LittleHelperAI.KingFactory.Pipeline.Storage.IPipelineExecutionStore, LittleHelperAI.Backend.Services.Pipeline.Storage.DatabasePipelineExecutionStore>();
builder.Services.AddScoped<LittleHelperAI.Services.Pipeline.IPipelineExecutionService, LittleHelperAI.Services.Pipeline.PipelineExecutionService>();

#endregion

var app = builder.Build();

#region Database Bootstrap

using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS `knowledge_entries` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `key` varchar(255) NOT NULL,
  `category` varchar(64) NOT NULL DEFAULT 'general',
  `answer` longtext NOT NULL,
  `aliases` longtext NULL,
  `confidence` double NOT NULL DEFAULT 0.6,
  `source` varchar(32) NOT NULL DEFAULT 'manual',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_used_at` datetime NULL,
  `times_used` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_knowledge_key` (`key`),
  KEY `ix_knowledge_entries_category` (`category`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
");

        db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS `learned_knowledge` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `normalized_key` varchar(255) NOT NULL,
  `question` longtext NOT NULL,
  `answer` longtext NOT NULL,
  `source` varchar(32) NULL,
  `confidence` double NOT NULL DEFAULT 0.55,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_used_at` datetime NULL,
  `times_used` int(11) NOT NULL DEFAULT 0,
  `last_verified_at` datetime NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_learned_normkey` (`normalized_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
");

        db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS `user_stripe_subscriptions` (
  `id` varchar(36) NOT NULL,
  `user_id` int(11) NOT NULL,
  `subscription_id` varchar(255) NULL,
  `price_id` varchar(255) NULL,
  `plan_id` int(11) NULL,
  `status` varchar(32) NOT NULL,
  `current_period_end_utc` datetime NOT NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `ix_user_stripe_subscriptions_user_id` (`user_id`),
  KEY `ix_user_stripe_subscriptions_status_end` (`status`, `current_period_end_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `stripeplan_policies` (
  `plan_id` int(11) NOT NULL,
  `plan_name` varchar(128) NOT NULL,
  `is_unlimited` tinyint(1) NOT NULL DEFAULT 0,
  `daily_credits` double NULL,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`plan_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `user_daily_credit_state` (
  `user_id` int(11) NOT NULL,
  `utc_day` date NOT NULL,
  `daily_allowance` double NOT NULL,
  `daily_remaining` double NOT NULL,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");

        db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS `user_notifications` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `user_id` INT NOT NULL,
  `title` VARCHAR(255) NOT NULL,
  `message` TEXT NOT NULL,
  `action_url` VARCHAR(512) NULL,
  `is_read` TINYINT(1) NOT NULL DEFAULT 0,
  `created_utc` DATETIME NOT NULL DEFAULT UTC_TIMESTAMP,
  `read_utc` DATETIME NULL,
  PRIMARY KEY (`id`),
  KEY `ix_user_notifications_user_id` (`user_id`),
  KEY `ix_user_notifications_user_read` (`user_id`,`is_read`),
  CONSTRAINT `fk_user_notifications_user` FOREIGN KEY (`user_id`)
    REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `admin_audit_log` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `admin_user_id` INT NOT NULL,
  `action` VARCHAR(64) NOT NULL,
  `entity` VARCHAR(64) NOT NULL,
  `entity_id` VARCHAR(128) NULL,
  `details` TEXT NOT NULL,
  `created_utc` DATETIME NOT NULL DEFAULT UTC_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `ix_admin_audit_created` (`created_utc`),
  KEY `ix_admin_audit_admin` (`admin_user_id`),
  CONSTRAINT `fk_admin_audit_user` FOREIGN KEY (`admin_user_id`)
    REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");

        db.Database.ExecuteSqlRaw(@"
INSERT IGNORE INTO `knowledge_entries` (`key`,`category`,`answer`,`aliases`,`confidence`,`source`)
VALUES
('hi','greeting','Hi! How can I help you today?','hello,hey,yo,hiya,hi there',0.95,'seed'),
('hello','greeting','Hello! What can I do for you?','hi,hey,hiya,hello there',0.95,'seed'),
('how are you','greeting','I''m doing well, thanks! What can I help with?','how r you,how you doing,how are u',0.9,'seed'),
('thanks','greeting','No worries! Anything else you need?','thank you,thx,cheers',0.9,'seed'),
('goodbye','greeting','Bye! Come back any time.','bye,see you,see ya,later',0.9,'seed');
");

        db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS `dead_letter_messages` (
  `id` varchar(36) NOT NULL,
  `user_id` int(11) NOT NULL,
  `chat_id` int(11) NULL,
  `conversation_id` varchar(36) NULL,
  `request_payload` longtext NOT NULL,
  `error_message` varchar(2048) NOT NULL,
  `error_type` varchar(256) NOT NULL,
  `stack_trace` longtext NULL,
  `retry_count` int(11) NOT NULL DEFAULT 0,
  `max_retries` int(11) NOT NULL DEFAULT 3,
  `status` varchar(32) NOT NULL DEFAULT 'Pending',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_retry_at` datetime NULL,
  `resolved_at` datetime NULL,
  `resolution_notes` text NULL,
  `metadata` longtext NULL,
  PRIMARY KEY (`id`),
  KEY `ix_dead_letter_user_id` (`user_id`),
  KEY `ix_dead_letter_status` (`status`),
  KEY `ix_dead_letter_created_at` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
");

        // Pipeline V2 tables
        db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS `pipelines_v2` (
  `id` varchar(36) NOT NULL,
  `name` varchar(255) NOT NULL,
  `description` text NULL,
  `version` varchar(20) NOT NULL DEFAULT '1.0.0',
  `status` varchar(20) NOT NULL DEFAULT 'Draft',
  `config_json` longtext NOT NULL,
  `is_primary` tinyint(1) NOT NULL DEFAULT 0,
  `tags` longtext NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` int(11) NULL,
  PRIMARY KEY (`id`),
  KEY `ix_pipelines_v2_status` (`status`),
  KEY `ix_pipelines_v2_is_primary` (`is_primary`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `pipeline_versions` (
  `id` varchar(36) NOT NULL,
  `pipeline_id` varchar(36) NOT NULL,
  `version` varchar(20) NOT NULL,
  `config_json` longtext NOT NULL,
  `commit_message` varchar(500) NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `created_by` int(11) NULL,
  PRIMARY KEY (`id`),
  KEY `ix_pipeline_versions_pipeline_id` (`pipeline_id`),
  KEY `ix_pipeline_versions_version` (`version`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `pipeline_executions` (
  `id` varchar(36) NOT NULL,
  `pipeline_id` varchar(36) NOT NULL,
  `conversation_id` varchar(36) NULL,
  `user_id` int(11) NULL,
  `status` varchar(20) NOT NULL,
  `started_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `completed_at` datetime NULL,
  `duration_ms` bigint(20) NULL,
  `step_count` int(11) NOT NULL DEFAULT 0,
  `completed_step_count` int(11) NOT NULL DEFAULT 0,
  `error_message` text NULL,
  `input_summary` varchar(500) NULL,
  `output_summary` varchar(500) NULL,
  PRIMARY KEY (`id`),
  KEY `ix_pipeline_executions_pipeline_id` (`pipeline_id`),
  KEY `ix_pipeline_executions_user_id` (`user_id`),
  KEY `ix_pipeline_executions_status` (`status`),
  KEY `ix_pipeline_executions_started_at` (`started_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `pipeline_step_logs` (
  `id` varchar(36) NOT NULL,
  `execution_id` varchar(36) NOT NULL,
  `step_id` varchar(100) NOT NULL,
  `step_type` varchar(100) NOT NULL,
  `step_order` int(11) NOT NULL,
  `status` varchar(20) NOT NULL,
  `started_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `completed_at` datetime NULL,
  `duration_ms` bigint(20) NULL,
  `input_json` longtext NULL,
  `output_json` longtext NULL,
  `error_message` text NULL,
  PRIMARY KEY (`id`),
  KEY `ix_pipeline_step_logs_execution_id` (`execution_id`),
  KEY `ix_pipeline_step_logs_step_order` (`step_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `pipeline_metrics` (
  `id` varchar(36) NOT NULL,
  `pipeline_id` varchar(36) NOT NULL,
  `date` date NOT NULL,
  `total_executions` int(11) NOT NULL DEFAULT 0,
  `success_count` int(11) NOT NULL DEFAULT 0,
  `failure_count` int(11) NOT NULL DEFAULT 0,
  `avg_duration_ms` bigint(20) NULL,
  `min_duration_ms` bigint(20) NULL,
  `max_duration_ms` bigint(20) NULL,
  `total_steps_executed` int(11) NOT NULL DEFAULT 0,
  `total_tool_calls` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_pipeline_metrics_pipeline_date` (`pipeline_id`, `date`),
  KEY `ix_pipeline_metrics_date` (`date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
");

        Console.WriteLine("Database tables verified/created successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"DB schema bootstrap failed: {ex.Message}");
    }
}

#endregion

#region Swagger (Dev Only)

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

#endregion

#region Middleware Pipeline

app.UseRouting();
app.UseCors("AllowBlazorFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapHub<ChatHub>("/hubs/chat");

#endregion

#region Health Check

app.MapGet("/", () => "LittleHelperAI Backend is running");

#endregion

app.Run();
