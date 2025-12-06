using Microsoft.AspNetCore.Mvc;
using LibGit2Sharp;
using System.IO;
using System.Collections.Concurrent;
using System.IO.Compression;
using StartUply.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using StartUply.Presentation.Hubs;

namespace StartUply.Presentation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectController : ControllerBase
    {
        private static ConcurrentDictionary<string, ProjectData> _projects = new();
        private static ConcurrentDictionary<string, ProgressStatus> _progressStore = new();
        private readonly IAIService _aiService;
        private readonly IHubContext<ProgressHub> _hubContext;

        public ProjectController(IAIService aiService, IHubContext<ProgressHub> hubContext)
        {
            _aiService = aiService;
            _hubContext = hubContext;
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

        [HttpPost("extract")]
        public async Task<IActionResult> ExtractStructure([FromBody] CloneRequest request)
        {
            string tempDir = null;
            try
            {
                var id = Guid.NewGuid().ToString();
                tempDir = Path.Combine(Path.GetTempPath(), id);
                Repository.Clone(request.Url, tempDir);

                // Find the actual repo directory (LibGit2Sharp might create a subdirectory)
                var repoDir = tempDir;
                var subDirs = Directory.GetDirectories(tempDir);
                if (subDirs.Length == 1 && !subDirs[0].EndsWith(".git"))
                {
                    repoDir = subDirs[0];
                }

                var structure = GetDirectoryStructure(repoDir);

                // Clean up immediately after getting structure
                Directory.Delete(tempDir, true);

                return Ok(new { structure });
            }
            catch (Exception ex)
            {
                // Clean up on error
                if (tempDir != null && Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("createBase")]
        public async Task<IActionResult> CreateBaseProject([FromBody] CreateBaseRequest request)
        {
            var taskId = Guid.NewGuid().ToString();
            var progressCallback = CreateProgressCallback(taskId, request.ConnectionId);

            var baseCode = await _aiService.GenerateBaseProjectAsync(request.Domain, progressCallback);
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

            return Ok(new { id, folders, taskId });
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

            var taskId = Guid.NewGuid().ToString();
            var progressCallback = CreateProgressCallback(taskId, request.ConnectionId);

            var code = ReadProjectCode(project.Path);
            var convertedCode = await _aiService.ConvertCodeAsync(code, request.FromDomain, request.TargetDomain, progressCallback);

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

            return Ok(new { convertedProjectId = newId, folders = newFolders, taskId });
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateBackend([FromBody] GenerateRequest request)
        {
            if (!_projects.TryGetValue(request.Id, out var project))
            {
                return NotFound(new { error = "Project not found" });
            }

            var taskId = Guid.NewGuid().ToString();
            var progressCallback = CreateProgressCallback(taskId, request.ConnectionId);

            var frontendCode = ReadProjectCode(project.Path);
            var backendCode = await _aiService.GenerateBackendAsync(frontendCode, request.TargetDomain, progressCallback);

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

            return Ok(new { backendProjectId = newId, folders = newFolders, taskId });
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

        [HttpGet("progress/{taskId}")]
        public IActionResult GetProgress(string taskId)
        {
            if (_progressStore.TryGetValue(taskId, out var progress))
            {
                return Ok(progress);
            }
            return NotFound(new { error = "Progress not found" });
        }

        [HttpPost("process")]
        public async Task<IActionResult> Process([FromBody] ProcessRequest request)
        {
            try
            {
                string? projectId = null;
                if (!string.IsNullOrEmpty(request.GithubUrl))
                {
                    // Clone
                    var id = Guid.NewGuid().ToString();
                    var tempDir = Path.Combine(Path.GetTempPath(), id);
                    Repository.Clone(request.GithubUrl, tempDir);
                    var folders = Directory.GetDirectories(tempDir).Select(Path.GetFileName).ToArray();
                    _projects[id] = new ProjectData { Path = tempDir, Folders = folders, CreatedAt = DateTime.UtcNow };
                    projectId = id;
                }

                if (request.Mode == "conversion")
                {
                    if (string.IsNullOrEmpty(projectId)) return BadRequest(new { error = "GithubUrl required for conversion" });
                    var taskId = Guid.NewGuid().ToString();
                    var progressCallback = CreateProgressCallback(taskId, request.ConnectionId);
                    var project = _projects[projectId];
                    var code = ReadProjectCode(project.Path);
                    var convertedCode = await _aiService.ConvertCodeAsync(code, request.FromFramework ?? "React", request.TargetFramework, progressCallback);
                    var convertedFiles = ParseConvertedFiles(convertedCode);
                    var newId = Guid.NewGuid().ToString();
                    var newTempDir = Path.Combine(Path.GetTempPath(), newId);
                    Directory.CreateDirectory(newTempDir);
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
                    return Ok(new { projectId = newId, folders = newFolders, taskId });
                }
                else if (request.Mode == "generate")
                {
                    if (request.Type == "backend")
                    {
                        if (string.IsNullOrEmpty(projectId)) return BadRequest(new { error = "GithubUrl required for backend generation" });
                        var taskId = Guid.NewGuid().ToString();
                        var progressCallback = CreateProgressCallback(taskId, request.ConnectionId);
                        var project = _projects[projectId];
                        var frontendCode = ReadProjectCode(project.Path);
                        var backendCode = await _aiService.GenerateBackendAsync(frontendCode, request.TargetFramework, progressCallback);
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
                        return Ok(new { projectId = newId, folders = newFolders, taskId });
                    }
                    else if (request.Type == "frontend")
                    {
                        var taskId = Guid.NewGuid().ToString();
                        var progressCallback = CreateProgressCallback(taskId, request.ConnectionId);
                        var baseCode = await _aiService.GenerateBaseProjectAsync(request.TargetFramework, progressCallback);
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
                        return Ok(new { projectId = id, folders, taskId });
                    }
                    else
                    {
                        return BadRequest(new { error = "Invalid type for generate" });
                    }
                }
                else
                {
                    return BadRequest(new { error = "Invalid mode" });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        private string ReadProjectCode(string path)
        {
            var files = Directory.GetFiles(path, "*.js", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(path, "*.ts", SearchOption.AllDirectories))
                        .Concat(Directory.GetFiles(path, "*.jsx", SearchOption.AllDirectories))
                        .Concat(Directory.GetFiles(path, "*.tsx", SearchOption.AllDirectories));
            return string.Join("\n", files.Select(f => $"---FILE: {Path.GetRelativePath(path, f)} ---\n{System.IO.File.ReadAllText(f)}"));
        }

        private DirectoryItem GetDirectoryStructure(string path)
        {
            var info = new DirectoryInfo(path);
            var item = new DirectoryItem
            {
                Name = info.Name,
                Type = "directory",
                Path = "",
                Children = new List<DirectoryItem>()
            };

            foreach (var dir in info.GetDirectories().OrderBy(d => d.Name))
            {
                // Skip common unwanted directories
                if (dir.Name.StartsWith('.') || dir.Name == "node_modules" || dir.Name == "dist" || dir.Name == "build")
                    continue;

                item.Children.Add(GetDirectoryStructure(dir.FullName));
            }

            foreach (var file in info.GetFiles().OrderBy(f => f.Name))
            {
                // Only include relevant file types
                if (IsRelevantFile(file.Extension))
                {
                    item.Children.Add(new DirectoryItem
                    {
                        Name = file.Name,
                        Type = "file",
                        Path = Path.GetRelativePath(path, file.FullName),
                        Children = null
                    });
                }
            }

            return item;
        }

        private bool IsRelevantFile(string extension)
        {
            var relevantExtensions = new[] { ".js", ".ts", ".jsx", ".tsx", ".json", ".html", ".css", ".scss", ".less", ".md", ".txt", ".yml", ".yaml", ".xml", ".cs", ".py", ".java", ".cpp", ".c", ".php", ".rb", ".go", ".rs", ".vue", ".svelte", ".dart", ".kt", ".swift" };
            return relevantExtensions.Contains(extension.ToLower());
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

        private Action<string, int> CreateProgressCallback(string taskId, string? connectionId)
        {
            return (message, percentage) =>
            {
                var progress = new ProgressStatus
                {
                    Message = message,
                    Percentage = percentage,
                    Timestamp = DateTime.UtcNow
                };
                _progressStore[taskId] = progress;

                if (!string.IsNullOrEmpty(connectionId))
                {
                    _hubContext.Clients.Client(connectionId).SendAsync("ReceiveProgress", message, percentage);
                }
            };
        }
    }

    public class CloneRequest
    {
        public string Url { get; set; }
    }

    public class CreateBaseRequest
    {
        public string Domain { get; set; }
        public string? ConnectionId { get; set; }
    }

    public class ConvertRequest
    {
        public string Id { get; set; }
        public string FromDomain { get; set; }
        public string TargetDomain { get; set; }
        public string? BaseProjectId { get; set; }
        public string? ConnectionId { get; set; }
    }

    public class GenerateRequest
    {
        public string Id { get; set; }
        public string TargetDomain { get; set; }
        public string? ConnectionId { get; set; }
    }

    public class ProjectData
    {
        public string Path { get; set; }
        public string[] Folders { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ProcessRequest
    {
        public string? GithubUrl { get; set; }
        public string Mode { get; set; }
        public string? Type { get; set; }
        public string TargetFramework { get; set; }
        public string? FromFramework { get; set; }
        public string? ConnectionId { get; set; }
    }

    public class ProgressStatus
    {
        public string Message { get; set; }
        public int Percentage { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class DirectoryItem
    {
        public string Name { get; set; }
        public string Type { get; set; } // "directory" or "file"
        public string Path { get; set; }
        public List<DirectoryItem> Children { get; set; }
    }
}