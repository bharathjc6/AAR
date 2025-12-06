// =============================================================================
// AAR.Api - Program.cs
// Main entry point for the ASP.NET Core Web API
// =============================================================================

using AAR.Api.Middleware;
using AAR.Application;
using AAR.Infrastructure;
using AAR.Infrastructure.Persistence;
using Asp.Versioning;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Serilog;

// =============================================================================
// Configure Serilog early for startup logging
// =============================================================================
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/aar-.log", 
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    Log.Information("Starting AAR API...");

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog
    builder.Host.UseSerilog();

    // =============================================================================
    // Configure Services
    // =============================================================================
    
    // Add application layer services
    builder.Services.AddApplicationServices();
    
    // Add infrastructure layer services
    builder.Services.AddInfrastructureServices(builder.Configuration);

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

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });

        options.AddPolicy("Production", policy =>
        {
            // TODO: Configure allowed origins for production
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? ["https://localhost:3000"];
            
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    });

    // Health Checks
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AarDbContext>("database");
    // TODO: Add Azure health checks when using Azure services
    // .AddAzureBlobStorage(...)
    // .AddAzureQueueStorage(...)

    // Configure request size limit for file uploads
    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = 100 * 1024 * 1024; // 100 MB
    });

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100 MB
    });

    // =============================================================================
    // Build Application
    // =============================================================================
    var app = builder.Build();

    // =============================================================================
    // Configure Middleware Pipeline
    // =============================================================================
    
    // Global exception handler
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // Swagger (development only in production)
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "AAR API v1");
            options.RoutePrefix = string.Empty; // Serve Swagger at root
        });
    }

    // CORS
    if (app.Environment.IsDevelopment())
    {
        app.UseCors();
    }
    else
    {
        app.UseCors("Production");
    }

    // Request logging
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    });

    // API Key authentication middleware
    app.UseMiddleware<ApiKeyAuthMiddleware>();

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

    // Controllers
    app.MapControllers();

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
    
    // Create a development API key
    var (apiKey, plainTextKey) = AAR.Domain.Entities.ApiKey.Create(
        "Development Key",
        DateTime.UtcNow.AddYears(1),
        "read,write,admin");
    
    db.ApiKeys.Add(apiKey);
    await db.SaveChangesAsync();
    
    Log.Information("Development API Key created: {ApiKey}", plainTextKey);
    Log.Warning("⚠️ This API key is for development only. Generate new keys for production.");
}
