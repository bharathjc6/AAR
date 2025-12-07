using AAR.Application;
using AAR.Application.Interfaces;
using AAR.Application.Services;
using AAR.Infrastructure;
using AAR.Worker.Agents;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("MassTransit", Serilog.Events.LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/worker-.log", 
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting AAR Worker Service with MassTransit");
    
    // Set environment variable to enable consumer registration
    Environment.SetEnvironmentVariable("MASSTRANSIT_REGISTER_CONSUMERS", "true");
    
    var builder = Host.CreateApplicationBuilder(args);
    
    // Replace default logging with Serilog
    builder.Services.AddSerilog();
    
    // Register application services
    builder.Services.AddApplicationServices();
    
    // Register infrastructure services (includes MassTransit consumers)
    builder.Services.AddInfrastructureServices(builder.Configuration);
    
    // Register agents
    builder.Services.AddScoped<IAnalysisAgent, StructureAgent>();
    builder.Services.AddScoped<IAnalysisAgent, CodeQualityAgent>();
    builder.Services.AddScoped<IAnalysisAgent, SecurityAgent>();
    builder.Services.AddScoped<IAnalysisAgent, ArchitectureAdvisorAgent>();
    builder.Services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
    builder.Services.AddScoped<IReportAggregator, ReportAggregator>();
    
    // MassTransit consumers are registered via AddInfrastructureServices
    // No need for AnalysisWorker hosted service - MassTransit handles message consumption
    
    var host = builder.Build();

    Log.Information("AAR Worker Service started successfully - listening for messages");
    
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
