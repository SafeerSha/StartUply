using System.Collections.Generic;
using System.IO;
using System.Linq;
using StartUply.Application.Interfaces;
using StartUply.Domain.Entities;

namespace StartUply.Infrastructure.Services
{
    public class ProjectChunkerService : IProjectChunkerService
    {
        public List<ProjectChunk> ChunkProject(string projectPath, string languageOrFramework)
        {
            var extensions = new[] { ".js", ".ts", ".jsx", ".tsx", ".cs", ".java", ".py" };
            var allFiles = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()) && 
                            !f.Contains("node_modules") && !f.Contains("bin") && !f.Contains("obj") && !f.Contains(".git"))
                .ToList();

            var chunks = new List<ProjectChunk>();

            // Phase 1 Heuristic Chunking Strategy: Models -> Utils -> Services -> Controllers/UI
            var modelsAndTypes = allFiles.Where(f => f.ToLower().Contains("model") || f.ToLower().Contains("type") || f.ToLower().Contains("entity") || f.ToLower().Contains("interface") || f.ToLower().Contains("dto")).ToList();
            var utilsAndCommon = allFiles.Where(f => f.ToLower().Contains("util") || f.ToLower().Contains("common") || f.ToLower().Contains("helper") || f.ToLower().Contains("config")).ToList();
            var servicesAndProviders = allFiles.Where(f => f.ToLower().Contains("service") || f.ToLower().Contains("provider") || f.ToLower().Contains("repository") || f.ToLower().Contains("handler")).ToList();
            
            var others = allFiles.Except(modelsAndTypes).Except(utilsAndCommon).Except(servicesAndProviders).ToList();

            if (modelsAndTypes.Any()) chunks.Add(new ProjectChunk { Order = 1, Name = "Models and Types", FilePaths = modelsAndTypes });
            if (utilsAndCommon.Any()) chunks.Add(new ProjectChunk { Order = 2, Name = "Utilities and Common", FilePaths = utilsAndCommon });
            if (servicesAndProviders.Any()) chunks.Add(new ProjectChunk { Order = 3, Name = "Services and Repositories", FilePaths = servicesAndProviders });
            if (others.Any()) chunks.Add(new ProjectChunk { Order = 4, Name = "Core, Controllers, and UI", FilePaths = others });

            return chunks;
        }
    }
}
