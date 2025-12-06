using AAR.Application;
using AAR.Application.Services;
using AAR.Infrastructure;
using AAR.Worker;
using AAR.Worker.Agents;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/worker-.log", 
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting AAR Worker Service");
    
    var builder = Host.CreateApplicationBuilder(args);
    
    // Replace default logging with Serilog
    builder.Services.AddSerilog();
    
    // Register application services
    builder.Services.AddApplicationServices();
    
    // Register infrastructure services
    builder.Services.AddInfrastructureServices(builder.Configuration);
    
    // Register agents
    builder.Services.AddScoped<IAnalysisAgent, StructureAgent>();
    builder.Services.AddScoped<IAnalysisAgent, CodeQualityAgent>();
    builder.Services.AddScoped<IAnalysisAgent, SecurityAgent>();
    builder.Services.AddScoped<IAnalysisAgent, ArchitectureAdvisorAgent>();
    builder.Services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
    builder.Services.AddScoped<IReportAggregator, ReportAggregator>();
    
    // Register the worker
    builder.Services.AddHostedService<AnalysisWorker>();
    
    var host = builder.Build();
    
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
