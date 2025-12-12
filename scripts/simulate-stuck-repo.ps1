# =============================================================================
# Script to simulate the Azure AD sample repo structure that causes stuck batch
# The repo has ~320+ files across 33 batches (10 files/batch)
# Batch 21 would be files 201-210
# =============================================================================

param(
    [string]$OutputPath = ".\test-repos\active-directory-simulation"
)

$ErrorActionPreference = "Stop"

Write-Host "Creating simulated Azure AD sample repo structure..."

# Clean up if exists
if (Test-Path $OutputPath) {
    Remove-Item -Path $OutputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

# The actual repo has multiple subprojects with complex C# files
$projects = @(
    "1-WebApp-OIDC",
    "2-WebApp-graph-user",
    "3-WebApp-multi-APIs",
    "4-WebApp-your-API",
    "5-WebApp-AuthZ",
    "6-WebApp-Advanced"
)

$fileIndex = 0
$targetFileCount = 330  # To hit ~33 batches of 10

foreach ($project in $projects) {
    $projectPath = Join-Path $OutputPath $project
    New-Item -ItemType Directory -Path $projectPath -Force | Out-Null
    
    # Create Controllers directory with complex C# files
    $controllersPath = Join-Path $projectPath "Controllers"
    New-Item -ItemType Directory -Path $controllersPath -Force | Out-Null
    
    for ($i = 1; $i -le 8; $i++) {
        $fileIndex++
        $controllerContent = @"
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using Microsoft.Graph;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace $project.Controllers
{
    /// <summary>
    /// Controller $i for handling various operations.
    /// This is a complex controller with multiple actions and dependencies.
    /// </summary>
    [Authorize]
    [AuthorizeForScopes(ScopeKeySection = "DownstreamApi:Scopes")]
    public class Controller$($i)Controller : Controller
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly ILogger<Controller$($i)Controller> _logger;
        
        public Controller$($i)Controller(
            GraphServiceClient graphServiceClient,
            ITokenAcquisition tokenAcquisition,
            ILogger<Controller$($i)Controller> logger)
        {
            _graphServiceClient = graphServiceClient;
            _tokenAcquisition = tokenAcquisition;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _graphServiceClient.Me.Request().GetAsync();
            return View(user);
        }

        [HttpGet]
        public async Task<IActionResult> GetDetails(string id)
        {
            _logger.LogInformation("Getting details for {Id}", id);
            // Complex logic here
            await Task.Delay(1);
            return Ok(new { Id = id, Timestamp = DateTime.UtcNow });
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Process([FromBody] ProcessRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            
            try
            {
                var result = await ProcessInternalAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request");
                return StatusCode(500, "Internal error");
            }
        }
        
        private async Task<ProcessResult> ProcessInternalAsync(ProcessRequest request)
        {
            // Simulate complex processing
            await Task.Delay(1);
            return new ProcessResult { Success = true };
        }
    }
    
    public class ProcessRequest
    {
        public string Id { get; set; }
        public Dictionary<string, object> Data { get; set; }
    }
    
    public class ProcessResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
"@
        $filePath = Join-Path $controllersPath "Controller$($i)Controller.cs"
        Set-Content -Path $filePath -Value $controllerContent -Encoding UTF8
    }
    
    # Create Services directory
    $servicesPath = Join-Path $projectPath "Services"
    New-Item -ItemType Directory -Path $servicesPath -Force | Out-Null
    
    for ($i = 1; $i -le 6; $i++) {
        $fileIndex++
        $serviceContent = @"
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace $project.Services
{
    /// <summary>
    /// Service $i implementation with various business logic.
    /// </summary>
    public interface IService$i
    {
        Task<ServiceResult> ExecuteAsync(ServiceRequest request, CancellationToken ct = default);
        Task<IEnumerable<Item>> GetItemsAsync(string filter, CancellationToken ct = default);
    }
    
    public class Service$($i)Implementation : IService$i
    {
        private readonly ILogger<Service$($i)Implementation> _logger;
        private readonly ITokenAcquisition _tokenAcquisition;
        
        public Service$($i)Implementation(
            ILogger<Service$($i)Implementation> logger,
            ITokenAcquisition tokenAcquisition)
        {
            _logger = logger;
            _tokenAcquisition = tokenAcquisition;
        }
        
        public async Task<ServiceResult> ExecuteAsync(ServiceRequest request, CancellationToken ct = default)
        {
            _logger.LogInformation("Executing service $i with request {RequestId}", request.Id);
            
            try
            {
                // Complex business logic
                var items = await GetItemsAsync(request.Filter, ct);
                var processed = items.Select(ProcessItem).ToList();
                
                return new ServiceResult
                {
                    Success = true,
                    ProcessedCount = processed.Count,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Operation cancelled for request {RequestId}", request.Id);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in service $i");
                return new ServiceResult { Success = false, Error = ex.Message };
            }
        }
        
        public async Task<IEnumerable<Item>> GetItemsAsync(string filter, CancellationToken ct = default)
        {
            await Task.Delay(10, ct); // Simulate async work
            return new List<Item>
            {
                new Item { Id = "1", Name = "Item 1" },
                new Item { Id = "2", Name = "Item 2" }
            };
        }
        
        private Item ProcessItem(Item item)
        {
            item.Processed = true;
            item.ProcessedAt = DateTime.UtcNow;
            return item;
        }
    }
    
    public class ServiceRequest
    {
        public string Id { get; set; }
        public string Filter { get; set; }
        public Dictionary<string, string> Parameters { get; set; }
    }
    
    public class ServiceResult
    {
        public bool Success { get; set; }
        public int ProcessedCount { get; set; }
        public DateTime Timestamp { get; set; }
        public string Error { get; set; }
    }
    
    public class Item
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Processed { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
"@
        $filePath = Join-Path $servicesPath "Service$($i).cs"
        Set-Content -Path $filePath -Value $serviceContent -Encoding UTF8
    }
    
    # Create Models directory
    $modelsPath = Join-Path $projectPath "Models"
    New-Item -ItemType Directory -Path $modelsPath -Force | Out-Null
    
    for ($i = 1; $i -le 4; $i++) {
        $fileIndex++
        $modelContent = @"
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace $project.Models
{
    /// <summary>
    /// Model $i for data representation.
    /// </summary>
    public class Model$i
    {
        [Key]
        public Guid Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Name { get; set; }
        
        public string Description { get; set; }
        
        public DateTime CreatedAt { get; set; }
        
        public DateTime? UpdatedAt { get; set; }
        
        public bool IsActive { get; set; }
        
        public ICollection<ChildModel$i> Children { get; set; }
    }
    
    public class ChildModel$i
    {
        public Guid Id { get; set; }
        public Guid ParentId { get; set; }
        public string Value { get; set; }
        public int Order { get; set; }
    }
}
"@
        $filePath = Join-Path $modelsPath "Model$($i).cs"
        Set-Content -Path $filePath -Value $modelContent -Encoding UTF8
    }
    
    # Create Views (cshtml files)
    $viewsPath = Join-Path $projectPath "Views"
    New-Item -ItemType Directory -Path "$viewsPath\Home" -Force | Out-Null
    New-Item -ItemType Directory -Path "$viewsPath\Shared" -Force | Out-Null
    
    for ($i = 1; $i -le 3; $i++) {
        $fileIndex++
        $viewContent = @"
@model IEnumerable<$project.Models.Model$i>
@{
    ViewData["Title"] = "View $i";
}

<h1>@ViewData["Title"]</h1>

<table class="table">
    <thead>
        <tr>
            <th>@Html.DisplayNameFor(model => model.Name)</th>
            <th>@Html.DisplayNameFor(model => model.Description)</th>
            <th>@Html.DisplayNameFor(model => model.CreatedAt)</th>
            <th></th>
        </tr>
    </thead>
    <tbody>
        @foreach (var item in Model)
        {
            <tr>
                <td>@Html.DisplayFor(modelItem => item.Name)</td>
                <td>@Html.DisplayFor(modelItem => item.Description)</td>
                <td>@Html.DisplayFor(modelItem => item.CreatedAt)</td>
                <td>
                    <a asp-action="Edit" asp-route-id="@item.Id">Edit</a> |
                    <a asp-action="Details" asp-route-id="@item.Id">Details</a> |
                    <a asp-action="Delete" asp-route-id="@item.Id">Delete</a>
                </td>
            </tr>
        }
    </tbody>
</table>
"@
        $filePath = Join-Path "$viewsPath\Home" "View$($i).cshtml"
        Set-Content -Path $filePath -Value $viewContent -Encoding UTF8
    }
    
    # Create Infrastructure files
    $infraPath = Join-Path $projectPath "Infrastructure"
    New-Item -ItemType Directory -Path $infraPath -Force | Out-Null
    
    for ($i = 1; $i -le 4; $i++) {
        $fileIndex++
        $infraContent = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace $project.Infrastructure
{
    /// <summary>
    /// Infrastructure component $i for dependency injection and configuration.
    /// </summary>
    public static class InfrastructureSetup$i
    {
        public static IServiceCollection AddInfrastructure$i(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddDbContext<AppDbContext$i>(options =>
            {
                options.UseSqlServer(
                    configuration.GetConnectionString("DefaultConnection"),
                    b => b.MigrationsAssembly(typeof(InfrastructureSetup$i).Assembly.FullName));
            });
            
            services.AddScoped<IRepository$i, Repository$i>();
            services.AddScoped<IUnitOfWork$i, UnitOfWork$i>();
            
            return services;
        }
    }
    
    public class AppDbContext$i : DbContext
    {
        public AppDbContext$i(DbContextOptions<AppDbContext$i> options) : base(options) { }
        
        public DbSet<Models.Model$i> Models$i { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Configure entities
        }
    }
    
    public interface IRepository$i
    {
        Task<Models.Model$i> GetByIdAsync(Guid id, CancellationToken ct = default);
    }
    
    public class Repository$i : IRepository$i
    {
        private readonly AppDbContext$i _context;
        
        public Repository$i(AppDbContext$i context)
        {
            _context = context;
        }
        
        public async Task<Models.Model$i> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return await _context.Models$i.FindAsync(new object[] { id }, ct);
        }
    }
    
    public interface IUnitOfWork$i
    {
        Task<int> SaveChangesAsync(CancellationToken ct = default);
    }
    
    public class UnitOfWork$i : IUnitOfWork$i
    {
        private readonly AppDbContext$i _context;
        
        public UnitOfWork$i(AppDbContext$i context)
        {
            _context = context;
        }
        
        public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            return await _context.SaveChangesAsync(ct);
        }
    }
}
"@
        $filePath = Join-Path $infraPath "InfrastructureSetup$($i).cs"
        Set-Content -Path $filePath -Value $infraContent -Encoding UTF8
    }
    
    # Create Program.cs and Startup.cs
    $fileIndex++
    $programContent = @"
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace $project
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
"@
    Set-Content -Path (Join-Path $projectPath "Program.cs") -Value $programContent -Encoding UTF8
    
    $fileIndex++
    $startupContent = @"
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

namespace $project
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMicrosoftIdentityWebAppAuthentication(Configuration)
                .EnableTokenAcquisitionToCallDownstreamApi()
                .AddMicrosoftGraph(Configuration.GetSection("DownstreamApi"))
                .AddInMemoryTokenCaches();

            services.AddControllersWithViews()
                .AddMicrosoftIdentityUI();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
"@
    Set-Content -Path (Join-Path $projectPath "Startup.cs") -Value $startupContent -Encoding UTF8
}

# Add some extra files to ensure we hit batch 21+
$extraPath = Join-Path $OutputPath "Common"
New-Item -ItemType Directory -Path $extraPath -Force | Out-Null

# Create problematic files that might cause issues
# 1. Very large single-line file (can cause tokenizer issues)
$fileIndex++
$longLineContent = "// " + ("A" * 100000)  # 100KB single line
Set-Content -Path (Join-Path $extraPath "LongLine.cs") -Value $longLineContent -Encoding UTF8

# 2. Files with unusual characters
$fileIndex++
$unicodeContent = @"
namespace Common
{
    // Unicode test: 你好世界 مرحبا 🌍
    public class UnicodeTest
    {
        public string GetGreeting() => "Hello 世界";
    }
}
"@
Set-Content -Path (Join-Path $extraPath "UnicodeTest.cs") -Value $unicodeContent -Encoding UTF8

# 3. Deeply nested class (complex for Roslyn)
$fileIndex++
$nestedContent = @"
namespace Common.Deeply.Nested.Namespace.Structure
{
    public class Level1
    {
        public class Level2
        {
            public class Level3
            {
                public class Level4
                {
                    public class Level5
                    {
                        public class Level6
                        {
                            public class Level7
                            {
                                public class Level8
                                {
                                    public void DeepMethod()
                                    {
                                        var x = new System.Collections.Generic.Dictionary<
                                            System.Collections.Generic.List<string>,
                                            System.Collections.Generic.Dictionary<int, 
                                                System.Collections.Generic.List<object>>>();
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
"@
Set-Content -Path (Join-Path $extraPath "DeeplyNested.cs") -Value $nestedContent -Encoding UTF8

# 4. Many small files to pad out to target
$helperPath = Join-Path $OutputPath "Helpers"
New-Item -ItemType Directory -Path $helperPath -Force | Out-Null

while ($fileIndex -lt $targetFileCount) {
    $fileIndex++
    $helperContent = @"
namespace Helpers
{
    public static class Helper$fileIndex
    {
        public static int GetValue$fileIndex() => $fileIndex;
        public static string GetName$fileIndex() => nameof(Helper$fileIndex);
    }
}
"@
    Set-Content -Path (Join-Path $helperPath "Helper$($fileIndex).cs") -Value $helperContent -Encoding UTF8
}

Write-Host "Created $fileIndex files in $OutputPath"
Write-Host "Expected batches: $([math]::Ceiling($fileIndex / 10))"

# Verify structure
$actualCount = (Get-ChildItem -Path $OutputPath -Recurse -File | Where-Object { $_.Extension -in ".cs", ".cshtml" }).Count
Write-Host "Actual file count: $actualCount"
