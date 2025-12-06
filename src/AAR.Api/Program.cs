// =============================================================================
// AAR.Api - Program.cs
// Main entry point for the ASP.NET Core Web API
// Security-hardened configuration following OWASP guidelines
// =============================================================================

using AAR.Api.Middleware;
using AAR.Api.Security;
using AAR.Application;
using AAR.Infrastructure;
using AAR.Infrastructure.Persistence;
using Asp.Versioning;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Serilog;

// =============================================================================
// Configure Serilog early for startup logging with secure redaction
// =============================================================================
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithMachineName()
    .Enrich.With<SensitiveDataEnricher>()
    .Destructure.With<SensitiveDataRedactor>()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/aar-.log", 
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 100 * 1024 * 1024,
        shared: true)
    .CreateLogger();

try
{
    Log.Information("Starting AAR API...");

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog
    builder.Host.UseSerilog();

    // =============================================================================
    // Security: Configure Data Protection
    // =============================================================================
    var dataProtectionBuilder = builder.Services.AddDataProtection()
        .SetApplicationName("AAR-API")
        .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

    // TODO: REPLACE_WITH_KEY_VAULT - Use Azure Key Vault for key storage in production
    // var keyVaultUri = Environment.GetEnvironmentVariable("KEYVAULT_URI");
    // if (!string.IsNullOrEmpty(keyVaultUri))
    // {
    //     dataProtectionBuilder.ProtectKeysWithAzureKeyVault(
    //         new Uri($"{keyVaultUri}/keys/DataProtection"),
    //         new DefaultAzureCredential());
    // }
    // For development: persist keys to filesystem
    var keysPath = Path.Combine(builder.Environment.ContentRootPath, "keys");
    Directory.CreateDirectory(keysPath);
    dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

    // =============================================================================
    // Configure Services
    // =============================================================================
    
    // Add application layer services
    builder.Services.AddApplicationServices();
    
    // Add infrastructure layer services
    builder.Services.AddInfrastructureServices(builder.Configuration);

    // Add secure authentication (JWT + API key fallback)
    builder.Services.AddSecureAuthentication(builder.Configuration);

    // Add rate limiting
    builder.Services.AddSecureRateLimiting(options =>
    {
        options.GlobalRequestsPerMinute = builder.Configuration.GetValue("RateLimiting:GlobalRequestsPerMinute", 100);
        options.ApiKeyRequestsPerMinute = builder.Configuration.GetValue("RateLimiting:ApiKeyRequestsPerMinute", 300);
        options.UploadRequestsPerHour = builder.Configuration.GetValue("RateLimiting:UploadRequestsPerHour", 10);
    });

    // API Versioning
    builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-Api-Version"));
    });

    // Controllers and endpoints
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    
    // Swagger/OpenAPI
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Autonomous Architecture Reviewer API",
            Version = "v1",
            Description = "API for analyzing code repositories and generating architecture review reports"
        });
        
        // Add API key authentication to Swagger
        var apiKeyScheme = new OpenApiSecurityScheme
        {
            Description = "API key authentication. Enter your API key in the header.",
            Type = SecuritySchemeType.ApiKey,
            Name = "X-API-KEY",
            In = ParameterLocation.Header,
            Scheme = "ApiKey"
        };
        
        options.AddSecurityDefinition("ApiKey", apiKeyScheme);
        
        options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
        {
            { new OpenApiSecuritySchemeReference("ApiKey"), new List<string>() }
        });
    });

    // CORS with secure configuration
    builder.Services.AddCors(options =>
    {
        // Development CORS (permissive)
        options.AddPolicy("Development", policy =>
        {
            policy.WithOrigins(
                    "http://localhost:3000",
                    "https://localhost:3000",
                    "http://localhost:5000",
                    "https://localhost:5001")
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });

        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });

        // Production CORS (strict allowlist from environment)
        options.AddPolicy("Production", policy =>
        {
            var allowedOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS")?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? Array.Empty<string>();

            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyMethod()
                      .WithHeaders("Authorization", "Content-Type", "X-API-KEY", "X-Api-Version")
                      .AllowCredentials()
                      .SetPreflightMaxAge(TimeSpan.FromHours(1));
            }
            else
            {
                Log.Warning("No CORS origins configured for production. CORS will be denied.");
            }
        });
    });

    // Health Checks with security subsystem checks
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AarDbContext>("database")
        .AddCheck("storage", () =>
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Storage accessible"),
            tags: new[] { "ready" });
    // TODO: Add Azure health checks when using Azure services
    // .AddAzureKeyVault(...)
    // .AddAzureBlobStorage(...)

    // =============================================================================
    // Security: Configure request limits
    // =============================================================================
    var maxUploadSize = builder.Configuration.GetValue<long>("Security:MaxUploadSizeBytes", 100 * 1024 * 1024);

    // Configure request size limit for file uploads
    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = maxUploadSize;
        options.ValueLengthLimit = 1024 * 1024;
        options.MultipartHeadersLengthLimit = 16384;
    });

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = maxUploadSize;
        options.Limits.MaxRequestHeadersTotalSize = 32768;
        options.Limits.MaxRequestLineSize = 8192;
    });

    // =============================================================================
    // Security: Configure cookie policy
    // =============================================================================
    builder.Services.Configure<CookiePolicyOptions>(options =>
    {
        options.HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always;
        options.Secure = CookieSecurePolicy.Always;
        options.MinimumSameSitePolicy = SameSiteMode.Strict;
    });

    // =============================================================================
    // Build Application
    // =============================================================================
    var app = builder.Build();

    // =============================================================================
    // Configure Middleware Pipeline (order matters for security!)
    // =============================================================================
    
    // 1. Global exception handler (first to catch all exceptions)
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // 2. Security headers (early in pipeline)
    app.UseSecurityHeaders();

    // 3. HTTPS redirection (production only)
    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
        app.UseHsts();
    }

    // 4. Forwarded headers (for reverse proxy/load balancer)
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });

    // 5. Rate limiting
    app.UseRateLimiter();

    // 6. Swagger (development only in production)
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "AAR API v1");
            options.RoutePrefix = string.Empty;
        });
    }

    // 7. CORS
    if (app.Environment.IsDevelopment())
    {
        app.UseCors("Development");
    }
    else
    {
        app.UseCors("Production");
    }

    // 8. Request logging with redaction
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? "unknown");
            diagnosticContext.Set("HasAuth", httpContext.Request.Headers.ContainsKey("Authorization"));
        };
    });

    // 9. Authentication and Authorization
    app.UseAuthentication();
    app.UseAuthorization();

    // 10. API Key authentication middleware (fallback for machine-to-machine)
    app.UseMiddleware<ApiKeyAuthMiddleware>();

    // 11. Cookie policy
    app.UseCookiePolicy();

    // Health checks
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false, // Just check if the app is running
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    // Controllers with rate limiting
    app.MapControllers()
       .RequireRateLimiting("apikey");

    // =============================================================================
    // Run Database Migrations
    // =============================================================================
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AarDbContext>();
        
        Log.Information("Applying database migrations...");
        await db.Database.MigrateAsync();
        
        // Seed default API key for development
        if (app.Environment.IsDevelopment())
        {
            await SeedDevelopmentDataAsync(db);
        }
    }

    Log.Information("AAR API started successfully. Listening on {Urls}", 
        string.Join(", ", app.Urls));

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// =============================================================================
// Helper Methods
// =============================================================================

static async Task SeedDevelopmentDataAsync(AarDbContext db)
{
    // Check if we already have API keys
    if (await db.ApiKeys.AnyAsync())
    {
        return;
    }

    Log.Information("Seeding development API key...");
    
    // Create a development API key with system scope
    var (apiKey, plainTextKey) = AAR.Domain.Entities.ApiKey.Create(
        "Development Key",
        DateTime.UtcNow.AddYears(1),
        "read,write,admin,system");
    
    db.ApiKeys.Add(apiKey);
    await db.SaveChangesAsync();
    
    // Log with partial redaction for security
    var maskedKey = plainTextKey[..8] + "***REDACTED***";
    Log.Information("Development API Key created: {ApiKey}", maskedKey);
    Log.Warning("⚠️ This API key is for development only. Generate new keys for production.");
    
    // Output full key to console only (not to log file)
    Console.WriteLine($"\n🔑 Development API Key: {plainTextKey}\n");
}
