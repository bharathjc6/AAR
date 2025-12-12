// AAR.Tests - Fixtures/TestZipGenerator.cs
// Utilities for generating test zip files programmatically

using System.IO.Compression;
using System.Text;

namespace AAR.Tests.Fixtures;

/// <summary>
/// Generates test zip files with configurable content for testing.
/// </summary>
public static class TestZipGenerator
{
    /// <summary>
    /// Creates a small zip file with a few simple code files
    /// </summary>
    public static byte[] CreateSmallRepoZip()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            AddTextFile(archive, "Program.cs", """
                using System;
                
                namespace SmallApp
                {
                    public class Program
                    {
                        public static void Main(string[] args)
                        {
                            Console.WriteLine("Hello, World!");
                        }
                    }
                }
                """);

            AddTextFile(archive, "Utils/StringHelper.cs", """
                namespace SmallApp.Utils
                {
                    public static class StringHelper
                    {
                        public static string Capitalize(string input)
                        {
                            if (string.IsNullOrEmpty(input)) return input;
                            return char.ToUpper(input[0]) + input.Substring(1);
                        }
                    }
                }
                """);

            AddTextFile(archive, "README.md", """
                # Small App
                A simple test application.
                """);
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Creates a medium-sized zip file with enough content to trigger RAG chunking
    /// </summary>
    public static byte[] CreateMediumRepoZip(int fileCount = 20)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // Add a project file
            AddTextFile(archive, "MediumProject.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                        <OutputType>Exe</OutputType>
                        <TargetFramework>net8.0</TargetFramework>
                    </PropertyGroup>
                </Project>
                """);

            // Add multiple service files (medium size ~20-50KB each)
            for (int i = 1; i <= fileCount; i++)
            {
                var content = GenerateMediumClassFile(i);
                AddTextFile(archive, $"Services/Service{i}.cs", content);
            }

            // Add some model files
            for (int i = 1; i <= 5; i++)
            {
                AddTextFile(archive, $"Models/Entity{i}.cs", GenerateModelFile(i));
            }

            // Add repository files
            for (int i = 1; i <= 5; i++)
            {
                AddTextFile(archive, $"Repositories/Repository{i}.cs", GenerateRepositoryFile(i));
            }
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Creates a zip file containing a single large file (>200KB)
    /// </summary>
    public static byte[] CreateLargeFileRepoZip(int targetSizeKb = 300)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            var largeContent = GenerateLargeFile(targetSizeKb);
            AddTextFile(archive, "LargeService.cs", largeContent);

            // Also add a small file for contrast
            AddTextFile(archive, "SmallHelper.cs", """
                public static class SmallHelper
                {
                    public static int Add(int a, int b) => a + b;
                }
                """);
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Creates a mixed-size repository with small, medium, and large files
    /// </summary>
    public static byte[] CreateMixedRepoZip()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // Small files (<10KB)
            for (int i = 1; i <= 5; i++)
            {
                AddTextFile(archive, $"Small/Helper{i}.cs", GenerateSmallFile(i));
            }

            // Medium files (10KB-200KB) 
            for (int i = 1; i <= 3; i++)
            {
                AddTextFile(archive, $"Medium/Service{i}.cs", GenerateMediumClassFile(i));
            }

            // Large file (>200KB)
            AddTextFile(archive, "Large/MassiveService.cs", GenerateLargeFile(250));
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Creates an empty zip file
    /// </summary>
    public static byte[] CreateEmptyZip()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // Empty archive
        }
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Creates an invalid/corrupt zip
    /// </summary>
    public static byte[] CreateInvalidZip()
    {
        return Encoding.UTF8.GetBytes("This is not a valid zip file content");
    }

    /// <summary>
    /// Creates a zip with many small files for load testing
    /// </summary>
    public static byte[] CreateManyFilesZip(int fileCount)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            for (int i = 1; i <= fileCount; i++)
            {
                var folder = $"folder{(i % 10) + 1}";
                AddTextFile(archive, $"{folder}/File{i}.cs", GenerateSmallFile(i));
            }
        }
        return memoryStream.ToArray();
    }

    private static void AddTextFile(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string GenerateSmallFile(int index)
    {
        return $@"namespace TestProject.Helpers
{{
    public static class Helper{index}
    {{
        public static string GetName() => ""Helper{index}"";
        
        public static int Calculate{index}(int value)
        {{
            return value * {index};
        }}
        
        public static bool Validate{index}(string input)
        {{
            return !string.IsNullOrEmpty(input);
        }}
    }}
}}";
    }

    private static string GenerateMediumClassFile(int index)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace TestProject.Services");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// Service{index} provides business logic for feature {index}.");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public class Service{index}");
        sb.AppendLine("    {");
        sb.AppendLine($"        private readonly ILogger<Service{index}> _logger;");
        sb.AppendLine($"        private readonly IRepository{index} _repository;");
        sb.AppendLine();
        sb.AppendLine($"        public Service{index}(ILogger<Service{index}> logger, IRepository{index} repository)");
        sb.AppendLine("        {");
        sb.AppendLine("            _logger = logger;");
        sb.AppendLine("            _repository = repository;");
        sb.AppendLine("        }");

        // Add many methods to increase file size
        for (int m = 1; m <= 30; m++)
        {
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// Processes operation {m} for service {index}");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        public async Task<Result{index}_{m}> Process{m}Async(Request{index}_{m} request)");
            sb.AppendLine("        {");
            sb.AppendLine($"            _logger.LogInformation(\"Processing {m} for service {index}\");");
            sb.AppendLine();
            sb.AppendLine("            // Validate input");
            sb.AppendLine("            if (request == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                throw new ArgumentNullException(nameof(request));");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            // Perform business logic");
            sb.AppendLine($"            var data = await _repository.GetDataAsync{m}(request.Id);");
            sb.AppendLine("            if (data == null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                return Result{index}_{m}.NotFound();");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            // Transform and return");
            sb.AppendLine($"            return new Result{index}_{m}");
            sb.AppendLine("            {");
            sb.AppendLine("                Success = true,");
            sb.AppendLine($"                Data = Transform(data, {m}),");
            sb.AppendLine("                Timestamp = DateTime.UtcNow");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GenerateModelFile(int index)
    {
        return $@"namespace TestProject.Models
{{
    public class Entity{index}
    {{
        public Guid Id {{ get; set; }}
        public string Name {{ get; set; }} = string.Empty;
        public string Description {{ get; set; }} = string.Empty;
        public DateTime CreatedAt {{ get; set; }}
        public DateTime? UpdatedAt {{ get; set; }}
        public bool IsActive {{ get; set; }}
        public int Priority {{ get; set; }}
        public decimal Value{index} {{ get; set; }}
        public List<string> Tags {{ get; set; }} = new();
        public Dictionary<string, object> Metadata {{ get; set; }} = new();
    }}
}}";
    }

    private static string GenerateRepositoryFile(int index)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace TestProject.Repositories");
        sb.AppendLine("{");
        sb.AppendLine($"    public interface IRepository{index}");
        sb.AppendLine("    {");
        sb.AppendLine($"        Task<Entity{index}?> GetByIdAsync(Guid id);");
        sb.AppendLine($"        Task<IEnumerable<Entity{index}>> GetAllAsync();");
        sb.AppendLine($"        Task<Entity{index}> CreateAsync(Entity{index} entity);");
        sb.AppendLine($"        Task UpdateAsync(Entity{index} entity);");
        sb.AppendLine("        Task DeleteAsync(Guid id);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public class Repository{index} : IRepository{index}");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly DbContext _context;");
        sb.AppendLine();
        sb.AppendLine($"        public Repository{index}(DbContext context)");
        sb.AppendLine("        {");
        sb.AppendLine("            _context = context;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        public async Task<Entity{index}?> GetByIdAsync(Guid id)");
        sb.AppendLine("        {");
        sb.AppendLine($"            return await _context.Set<Entity{index}>()");
        sb.AppendLine("                .FirstOrDefaultAsync(e => e.Id == id);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        public async Task<IEnumerable<Entity{index}>> GetAllAsync()");
        sb.AppendLine("        {");
        sb.AppendLine($"            return await _context.Set<Entity{index}>().ToListAsync();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        public async Task<Entity{index}> CreateAsync(Entity{index} entity)");
        sb.AppendLine("        {");
        sb.AppendLine($"            _context.Set<Entity{index}>().Add(entity);");
        sb.AppendLine("            await _context.SaveChangesAsync();");
        sb.AppendLine("            return entity;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        public async Task UpdateAsync(Entity{index} entity)");
        sb.AppendLine("        {");
        sb.AppendLine($"            _context.Set<Entity{index}>().Update(entity);");
        sb.AppendLine("            await _context.SaveChangesAsync();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public async Task DeleteAsync(Guid id)");
        sb.AppendLine("        {");
        sb.AppendLine("            var entity = await GetByIdAsync(id);");
        sb.AppendLine("            if (entity != null)");
        sb.AppendLine("            {");
        sb.AppendLine($"                _context.Set<Entity{index}>().Remove(entity);");
        sb.AppendLine("                await _context.SaveChangesAsync();");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateLargeFile(int targetSizeKb)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// This is a large code file for testing");
        sb.AppendLine("namespace LargeProject.Services");
        sb.AppendLine("{");

        int classIndex = 0;
        while (sb.Length < targetSizeKb * 1024)
        {
            classIndex++;
            sb.AppendLine($"    public class LargeService{classIndex}");
            sb.AppendLine("    {");
            sb.AppendLine($"        private readonly ILogger _logger;");
            
            for (int m = 1; m <= 25; m++)
            {
                sb.AppendLine();
                sb.AppendLine($"        public async Task<Result> Method{classIndex}_{m}(Input input)");
                sb.AppendLine("        {");
                sb.AppendLine($"            _logger.LogInformation(\"Executing {classIndex}_{m}\");");
                sb.AppendLine("            var result = await ProcessInput(input);");
                sb.AppendLine("            return result.IsValid ? Result.Success() : Result.Failure();");
                sb.AppendLine("        }");
            }
            
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
