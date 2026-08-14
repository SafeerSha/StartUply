using Microsoft.AspNetCore.Mvc;
using LibGit2Sharp;
using System.IO;
using System.Collections.Concurrent;
using System.IO.Compression;
using StartUply.Application.Interfaces;
using StartUply.Application.Common;
using Microsoft.AspNetCore.SignalR;
using StartUply.Presentation.Hubs;

public class AuthenticationRequiredException : Exception
{
    public AuthenticationRequiredException(string message) : base(message) { }
}

public class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException(string message) : base(message) { }
}

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

        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            return Ok(new { status = "online", timestamp = DateTime.UtcNow });
        }

        [HttpPost("clone")]
        public async Task<IActionResult> CloneRepo([FromBody] CloneRequest request)
        {
            try
            {
                var id = Guid.NewGuid().ToString();
                var tempDir = Path.Combine(Path.GetTempPath(), id);
                CloneRepository(request.Url, tempDir, request.Username, request.Password);
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
                CloneRepository(request.Url, tempDir, request.Username, request.Password);

                // Find the actual repo directory (LibGit2Sharp might create a subdirectory)
                var repoDir = tempDir;
                var subDirs = Directory.GetDirectories(tempDir);
                if (subDirs.Length == 1 && !subDirs[0].EndsWith(".git"))
                {
                    repoDir = subDirs[0];
                }

                var structure = GetDirectoryStructure(repoDir);
                var detectedTech = DetectTechStack(repoDir);

                // Clean up immediately after getting structure
                Directory.Delete(tempDir, true);

                return Ok(new { structure, detectedTech });
            }
            catch (AuthenticationRequiredException ex)
            {
                if (tempDir != null && Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                return StatusCode(401, new { error = ex.Message });
            }
            catch (InvalidCredentialsException ex)
            {
                if (tempDir != null && Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
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

            // Clean up zip after response
            Response.OnCompleted(() =>
            {
                try
                {
                    stream.Dispose();
                    System.IO.File.Delete(zipPath);
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
                    CloneRepository(request.GithubUrl, tempDir, request.Username, request.Password);
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
                    var convertedCode = await _aiService.ConvertCodeAsync(code, request.FromFramework ?? "React", request.TargetFramework, progressCallback, request.AiApiKey);
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
                        var backendCode = await _aiService.GenerateBackendAsync(frontendCode, request.TargetFramework, progressCallback, request.AiApiKey);
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
                        var baseCode = await _aiService.GenerateBaseProjectAsync(request.TargetFramework, progressCallback, request.AiApiKey);
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
            catch (AuthenticationRequiredException ex)
            {
                return StatusCode(401, new { error = ex.Message });
            }
            catch (InvalidCredentialsException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (StartUply.Infrastructure.Services.GeminiRateLimitException ex)
            {
                return StatusCode(429, new { error = ex.Message, isRateLimit = true });
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Rate limit", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(429, new { error = "Gemini free tier rate limit reached. Please wait a moment or use your own Gemini API key (BYOK).", isRateLimit = true });
                }
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

        private DetectedTechInfo DetectTechStack(string repoDir)
        {
            try
            {
                var files = Directory.GetFiles(repoDir, "*", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(".git") && !f.Contains("node_modules") && !f.Contains("bin") && !f.Contains("obj") && !f.Contains(".next"))
                    .ToList();

                // 1. Check Node / JS / TS ecosystem via package.json
                var packageJsonFile = files.FirstOrDefault(f => Path.GetFileName(f).Equals("package.json", StringComparison.OrdinalIgnoreCase));
                if (packageJsonFile != null)
                {
                    try
                    {
                        var content = System.IO.File.ReadAllText(packageJsonFile);
                        if (content.Contains("\"next\""))
                            return new DetectedTechInfo { Name = "Next.js", Category = "frontend", Confidence = "high", Summary = "Next.js Project" };
                        if (content.Contains("\"nuxt\"") || content.Contains("\"@nuxt/"))
                            return new DetectedTechInfo { Name = "Nuxt.js", Category = "frontend", Confidence = "high", Summary = "Nuxt.js Project" };
                        if (content.Contains("\"@sveltejs/kit\""))
                            return new DetectedTechInfo { Name = "SvelteKit", Category = "frontend", Confidence = "high", Summary = "SvelteKit Project" };
                        if (content.Contains("\"@nestjs/core\""))
                            return new DetectedTechInfo { Name = "NestJS", Category = "backend", Confidence = "high", Summary = "NestJS Backend" };
                        if (content.Contains("\"express\""))
                            return new DetectedTechInfo { Name = "Express", Category = "backend", Confidence = "high", Summary = "Express Backend" };
                        if (content.Contains("\"fastify\""))
                            return new DetectedTechInfo { Name = "Fastify", Category = "backend", Confidence = "high", Summary = "Fastify Backend" };
                        if (content.Contains("\"hono\""))
                            return new DetectedTechInfo { Name = "Hono", Category = "backend", Confidence = "high", Summary = "Hono Backend" };
                        if (content.Contains("\"react\"") || content.Contains("\"react-dom\""))
                            return new DetectedTechInfo { Name = "React", Category = "frontend", Confidence = "high", Summary = "React Project" };
                        if (content.Contains("\"@angular/core\""))
                            return new DetectedTechInfo { Name = "Angular", Category = "frontend", Confidence = "high", Summary = "Angular Project" };
                        if (content.Contains("\"vue\""))
                            return new DetectedTechInfo { Name = "Vue.js", Category = "frontend", Confidence = "high", Summary = "Vue.js Project" };
                        if (content.Contains("\"svelte\""))
                            return new DetectedTechInfo { Name = "Svelte", Category = "frontend", Confidence = "high", Summary = "Svelte Project" };
                    }
                    catch { }
                }

                // 2. Check Java / Kotlin / Spring Boot
                if (files.Any(f => Path.GetFileName(f).Equals("pom.xml", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("build.gradle", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase)))
                {
                    var isSpring = files.Any(f => {
                        if (Path.GetFileName(f).Equals("pom.xml", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).EndsWith(".gradle", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).EndsWith(".gradle.kts", StringComparison.OrdinalIgnoreCase))
                        {
                            var text = System.IO.File.ReadAllText(f);
                            return text.Contains("spring-boot") || text.Contains("springframework");
                        }
                        return false;
                    });

                    if (isSpring)
                        return new DetectedTechInfo { Name = "Spring Boot", Category = "backend", Confidence = "high", Summary = "Spring Boot Backend" };

                    return new DetectedTechInfo { Name = "Spring Boot", Category = "backend", Confidence = "medium", Summary = "Spring Boot Backend" };
                }

                // 3. Check C# / .NET / ASP.NET Core
                if (files.Any(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)))
                {
                    return new DetectedTechInfo { Name = ".NET / ASP.NET Core", Category = "backend", Confidence = "high", Summary = ".NET / ASP.NET Core Backend" };
                }

                // 4. Check Python (FastAPI, Django, Flask)
                if (files.Any(f => Path.GetFileName(f).Equals("requirements.txt", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("Pipfile", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var pyReq in files.Where(f => Path.GetFileName(f).Equals("requirements.txt", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)))
                    {
                        try {
                            var text = System.IO.File.ReadAllText(pyReq).ToLower();
                            if (text.Contains("fastapi"))
                                return new DetectedTechInfo { Name = "FastAPI", Category = "backend", Confidence = "high", Summary = "FastAPI Backend" };
                            if (text.Contains("django"))
                                return new DetectedTechInfo { Name = "Django", Category = "backend", Confidence = "high", Summary = "Django Project" };
                            if (text.Contains("flask"))
                                return new DetectedTechInfo { Name = "Flask", Category = "backend", Confidence = "high", Summary = "Flask Backend" };
                        } catch {}
                    }
                    return new DetectedTechInfo { Name = "FastAPI", Category = "backend", Confidence = "medium", Summary = "Python Backend" };
                }

                // 5. Check PHP (Laravel, Symfony)
                var composerJson = files.FirstOrDefault(f => Path.GetFileName(f).Equals("composer.json", StringComparison.OrdinalIgnoreCase));
                if (composerJson != null)
                {
                    try {
                        var text = System.IO.File.ReadAllText(composerJson).ToLower();
                        if (text.Contains("laravel/framework"))
                            return new DetectedTechInfo { Name = "Laravel", Category = "backend", Confidence = "high", Summary = "Laravel Backend" };
                        if (text.Contains("symfony"))
                            return new DetectedTechInfo { Name = "Symfony", Category = "backend", Confidence = "high", Summary = "Symfony Backend" };
                    } catch {}
                    return new DetectedTechInfo { Name = "Laravel", Category = "backend", Confidence = "medium", Summary = "PHP Backend" };
                }

                // 6. Check Ruby (Rails)
                if (files.Any(f => Path.GetFileName(f).Equals("Gemfile", StringComparison.OrdinalIgnoreCase)))
                {
                    return new DetectedTechInfo { Name = "Ruby on Rails", Category = "backend", Confidence = "high", Summary = "Ruby on Rails Backend" };
                }

                // 7. Check Go (Gin, Fiber, Echo)
                if (files.Any(f => Path.GetFileName(f).Equals("go.mod", StringComparison.OrdinalIgnoreCase)))
                {
                    return new DetectedTechInfo { Name = "Gin", Category = "backend", Confidence = "medium", Summary = "Go Backend" };
                }

                // 8. Extension Fallback
                if (files.Any(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                    return new DetectedTechInfo { Name = ".NET / ASP.NET Core", Category = "backend", Confidence = "medium", Summary = ".NET / ASP.NET Core Backend" };
                if (files.Any(f => f.EndsWith(".java", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".kt", StringComparison.OrdinalIgnoreCase)))
                    return new DetectedTechInfo { Name = "Spring Boot", Category = "backend", Confidence = "medium", Summary = "Spring Boot Backend" };
                if (files.Any(f => f.EndsWith(".py", StringComparison.OrdinalIgnoreCase)))
                    return new DetectedTechInfo { Name = "FastAPI", Category = "backend", Confidence = "medium", Summary = "Python Backend" };
                if (files.Any(f => f.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)))
                    return new DetectedTechInfo { Name = "React", Category = "frontend", Confidence = "medium", Summary = "React Project" };

                return new DetectedTechInfo { Name = "Unknown", Category = "general", Confidence = "low", Summary = "Unknown Tech" };
            }
            catch (Exception)
            {
                return new DetectedTechInfo { Name = "Unknown", Category = "general", Confidence = "low", Summary = "Unknown Tech" };
            }
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

        private void CloneRepository(string url, string path, string? username, string? password)
        {
            try
            {
                // First, try to clone without credentials (for public repos)
                Repository.Clone(url, path);
            }
            catch (LibGit2SharpException ex) when (IsAuthenticationError(ex))
            {
                // If authentication failed, retry with credentials or Personal Access Token if provided
                var effectiveUser = !string.IsNullOrEmpty(username) ? username : (!string.IsNullOrEmpty(password) ? "x-access-token" : null);
                var effectivePass = !string.IsNullOrEmpty(password) ? password : username;

                if (!string.IsNullOrEmpty(effectiveUser) && !string.IsNullOrEmpty(effectivePass))
                {
                    try
                    {
                        var cloneOptions = new CloneOptions(new FetchOptions
                        {
                            CredentialsProvider = (_url, _user, _cred) =>
                                new UsernamePasswordCredentials { Username = effectiveUser, Password = effectivePass }
                        });
                        Repository.Clone(url, path, cloneOptions);
                    }
                    catch (LibGit2SharpException retryEx)
                    {
                        var sanitizedMsg = SecurityUtils.MaskSecret(retryEx.Message);
                        throw new InvalidCredentialsException($"Authentication failed for private repository. Please verify that your Personal Access Token (PAT) is valid and has 'repo' scope.");
                    }
                }
                else
                {
                    // No credentials provided for private repo
                    throw new AuthenticationRequiredException("This repository is private or requires authentication. Please provide a Personal Access Token (PAT).");
                }
            }
        }

        private bool IsAuthenticationError(LibGit2SharpException ex)
        {
            var message = ex.Message.ToLower();
            return message.Contains("authentication") ||
                   message.Contains("credentials") ||
                   message.Contains("unauthorized") ||
                   message.Contains("permission denied") ||
                   message.Contains("auth");
        }

        [HttpPost("pushToGithub")]
        public async Task<IActionResult> PushToGithub([FromBody] PushToGithubRequest request)
        {
            if (!_projects.TryGetValue(request.Id, out var project))
            {
                return NotFound(new { error = "Project session not found or expired." });
            }

            if (string.IsNullOrWhiteSpace(request.GithubToken))
            {
                return BadRequest(new { error = "GitHub Personal Access Token is required." });
            }

            if (string.IsNullOrWhiteSpace(request.RepoName))
            {
                return BadRequest(new { error = "Repository name is required." });
            }

            try
            {
                var progressCallback = CreateProgressCallback(Guid.NewGuid().ToString(), request.ConnectionId);
                progressCallback?.Invoke("Creating GitHub repository...", 20);

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StartUply-App");
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.GithubToken);

                var createRepoPayload = new
                {
                    name = request.RepoName,
                    @private = request.IsPrivate,
                    description = request.Description ?? "Created with TranspileAI / StartUply",
                    auto_init = false
                };

                var response = await httpClient.PostAsJsonAsync("https://api.github.com/user/repos", createRepoPayload);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return BadRequest(new { error = $"Failed to create GitHub repository: {response.StatusCode} - {errorContent}" });
                }

                var repoResponse = await response.Content.ReadFromJsonAsync<GithubRepoResponse>();
                var cloneUrl = repoResponse?.clone_url ?? $"https://github.com/{request.RepoName}.git";
                var htmlUrl = repoResponse?.html_url ?? $"https://github.com/{request.RepoName}";

                progressCallback?.Invoke("Initializing local git repository...", 50);

                var gitDir = Path.Combine(project.Path, ".git");
                if (!Directory.Exists(gitDir))
                {
                    Repository.Init(project.Path);
                }

                using (var repo = new Repository(project.Path))
                {
                    progressCallback?.Invoke("Staging files and committing...", 70);

                    Commands.Stage(repo, "*");

                    var author = new Signature("TranspileAI", "transpileai@startuply.com", DateTimeOffset.Now);

                    if (repo.RetrieveStatus().IsDirty)
                    {
                        repo.Commit("Initial commit from TranspileAI", author, author);
                    }

                    progressCallback?.Invoke("Pushing code to GitHub...", 85);

                    if (repo.Network.Remotes["origin"] != null)
                    {
                        repo.Network.Remotes.Update("origin", r => r.Url = cloneUrl);
                    }
                    else
                    {
                        repo.Network.Remotes.Add("origin", cloneUrl);
                    }

                    var branch = repo.Head;

                    if (branch.FriendlyName != "main")
                    {
                        branch = repo.Branches.Rename(branch, "main");
                    }

                    var remote = repo.Network.Remotes["origin"];

                    var options = new PushOptions
                    {
                        CredentialsProvider = (_url, _user, _cred) =>
                            new UsernamePasswordCredentials
                            {
                                Username = "x-access-token",
                                Password = request.GithubToken
                            }
                    };

                    repo.Network.Push(remote, @"refs/heads/main:refs/heads/main", options);

                    repo.Branches.Update(branch,
                        b => b.Remote = "origin",
                        b => b.UpstreamBranch = branch.CanonicalName);
                }

                progressCallback?.Invoke("Repository created and code pushed successfully!", 100);

                return Ok(new { success = true, repoUrl = htmlUrl, cloneUrl });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }

    public class CloneRequest
    {
        public string Url { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
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
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? AiApiKey { get; set; }
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

    public class PushToGithubRequest
    {
        public string Id { get; set; }
        public string RepoName { get; set; }
        public bool IsPrivate { get; set; }
        public string? Description { get; set; }
        public string GithubToken { get; set; }
        public string? ConnectionId { get; set; }
    }

    public class GithubRepoResponse
    {
        public string? html_url { get; set; }
        public string? clone_url { get; set; }
    }

    public class DetectedTechInfo
    {
        public string Name { get; set; } = "Unknown";
        public string Category { get; set; } = "general";
        public string Confidence { get; set; } = "medium";
        public string Summary { get; set; } = "";
    }
}