using Microsoft.AspNetCore.Mvc;
using LibGit2Sharp;
using System.IO;
using System.Collections.Concurrent;
using System.IO.Compression;
using StartUply.Application.Interfaces;

namespace StartUply.Presentation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectController : ControllerBase
    {
        private static ConcurrentDictionary<string, ProjectData> _projects = new();
        private readonly IAIService _aiService;

        public ProjectController(IAIService aiService)
        {
            _aiService = aiService;
        }

        [HttpPost("clone")]
        public async Task<IActionResult> CloneRepo([FromBody] CloneRequest request)
        {
            try
            {
                var id = Guid.NewGuid().ToString();
                var tempDir = Path.Combine(Path.GetTempPath(), id);
                Repository.Clone(request.Url, tempDir);
                var folders = Directory.GetDirectories(tempDir).Select(Path.GetFileName).ToArray();
                _projects[id] = new ProjectData { Path = tempDir, Folders = folders, CreatedAt = DateTime.UtcNow };
                return Ok(new { id, folders });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("createBase")]
        public async Task<IActionResult> CreateBaseProject([FromBody] CreateBaseRequest request)
        {
            var baseCode = await _aiService.GenerateBaseProjectAsync(request.Domain);
            var convertedFiles = ParseConvertedFiles(baseCode);
            var id = Guid.NewGuid().ToString();
            var tempDir = Path.Combine(Path.GetTempPath(), id);
            Directory.CreateDirectory(tempDir);

            foreach (var kvp in convertedFiles)
            {
                var relativePath = kvp.Key;
                var content = kvp.Value;
                var fullPath = Path.Combine(tempDir, relativePath);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                System.IO.File.WriteAllText(fullPath, content);
            }

            var folders = Directory.GetDirectories(tempDir).Select(Path.GetFileName).ToArray();
            _projects[id] = new ProjectData { Path = tempDir, Folders = folders, CreatedAt = DateTime.UtcNow };

            return Ok(new { id, folders });
        }

        [HttpPost("convert")]
        public async Task<IActionResult> ConvertProject([FromBody] ConvertRequest request)
        {
            if (!_projects.TryGetValue(request.Id, out var project))
            {
                return NotFound(new { error = "Project not found" });
            }

            string newTempDir;
            string newId;

            if (!string.IsNullOrEmpty(request.BaseProjectId))
            {
                if (!_projects.TryGetValue(request.BaseProjectId, out var baseProject))
                {
                    return NotFound(new { error = "Base project not found" });
                }
                newTempDir = baseProject.Path;
                newId = request.BaseProjectId;
            }
            else
            {
                newId = Guid.NewGuid().ToString();
                newTempDir = Path.Combine(Path.GetTempPath(), newId);
                Directory.CreateDirectory(newTempDir);
            }

            var code = ReadProjectCode(project.Path);
            var convertedCode = await _aiService.ConvertCodeAsync(code, request.FromDomain, request.TargetDomain);

            var convertedFiles = ParseConvertedFiles(convertedCode);

            foreach (var kvp in convertedFiles)
            {
                var relativePath = kvp.Key;
                var content = kvp.Value;
                var fullPath = Path.Combine(newTempDir, relativePath);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                System.IO.File.WriteAllText(fullPath, content);
            }

            var newFolders = Directory.GetDirectories(newTempDir).Select(Path.GetFileName).ToArray();
            _projects[newId] = new ProjectData { Path = newTempDir, Folders = newFolders, CreatedAt = DateTime.UtcNow };

            return Ok(new { convertedProjectId = newId, folders = newFolders });
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateBackend([FromBody] GenerateRequest request)
        {
            if (!_projects.TryGetValue(request.Id, out var project))
            {
                return NotFound(new { error = "Project not found" });
            }

            var frontendCode = ReadProjectCode(project.Path);
            var backendCode = await _aiService.GenerateBackendAsync(frontendCode, request.TargetDomain);

            var backendFiles = ParseConvertedFiles(backendCode);
            var newId = Guid.NewGuid().ToString();
            var newTempDir = Path.Combine(Path.GetTempPath(), newId);
            Directory.CreateDirectory(newTempDir);

            foreach (var kvp in backendFiles)
            {
                var relativePath = kvp.Key;
                var content = kvp.Value;
                var fullPath = Path.Combine(newTempDir, relativePath);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                System.IO.File.WriteAllText(fullPath, content);
            }

            var newFolders = Directory.GetDirectories(newTempDir).Select(Path.GetFileName).ToArray();
            _projects[newId] = new ProjectData { Path = newTempDir, Folders = newFolders, CreatedAt = DateTime.UtcNow };

            return Ok(new { backendProjectId = newId, folders = newFolders });
        }

        [HttpGet("download/{id}")]
        public IActionResult DownloadProject(string id)
        {
            if (!_projects.TryGetValue(id, out var project))
            {
                return NotFound(new { error = "Project not found" });
            }

            var zipPath = Path.Combine(Path.GetTempPath(), $"{id}.zip");
            ZipFile.CreateFromDirectory(project.Path, zipPath);

            var stream = System.IO.File.OpenRead(zipPath);
            var result = File(stream, "application/zip", "project.zip");

            // Clean up after response
            Response.OnCompleted(() =>
            {
                try
                {
                    stream.Dispose();
                    System.IO.File.Delete(zipPath);
                    Directory.Delete(project.Path, true);
                    _projects.TryRemove(id, out _);
                }
                catch { }
                return Task.CompletedTask;
            });

            return result;
        }

        private string ReadProjectCode(string path)
        {
            var files = Directory.GetFiles(path, "*.js", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(path, "*.ts", SearchOption.AllDirectories))
                        .Concat(Directory.GetFiles(path, "*.jsx", SearchOption.AllDirectories))
                        .Concat(Directory.GetFiles(path, "*.tsx", SearchOption.AllDirectories));
            return string.Join("\n", files.Select(f => $"---FILE: {Path.GetRelativePath(path, f)} ---\n{System.IO.File.ReadAllText(f)}"));
        }

        private Dictionary<string, string> ParseConvertedFiles(string response)
        {
            var files = new Dictionary<string, string>();
            var parts = response.Split(new[] { "---FILE:" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var lines = part.Split('\n', 2);
                if (lines.Length >= 2)
                {
                    var path = lines[0].Trim();
                    var content = lines[1];
                    files[path] = content;
                }
            }
            return files;
        }
    }

    public class CloneRequest
    {
        public string Url { get; set; }
    }

    public class CreateBaseRequest
    {
        public string Domain { get; set; }
    }

    public class ConvertRequest
    {
        public string Id { get; set; }
        public string FromDomain { get; set; }
        public string TargetDomain { get; set; }
        public string? BaseProjectId { get; set; }
    }

    public class GenerateRequest
    {
        public string Id { get; set; }
        public string TargetDomain { get; set; }
    }

    public class ProjectData
    {
        public string Path { get; set; }
        public string[] Folders { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}