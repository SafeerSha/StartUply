using StartUply.Application.Interfaces;
using StartUply.Application.Common;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace StartUply.Infrastructure.Services
{
    public class GeminiRateLimitException : Exception
    {
        public GeminiRateLimitException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    public class AIService : IAIService
    {
        private static readonly ConcurrentDictionary<string, string> _sha256Cache = new();
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _modelId;

        public AIService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
            _apiKey = configuration["Gemini:ApiKey"] 
                      ?? configuration["OpenRouter:ApiKey"] 
                      ?? throw new ArgumentNullException("Gemini:ApiKey is not configured in appsettings or environment variables.");
            _modelId = configuration["Gemini:Model"] ?? "gemini-3.5-flash-lite";
        }

        public async Task<string> ConvertCodeAsync(string code, string fromDomain, string toDomain, Action<string, int>? progressCallback = null, string? customAiApiKey = null)
        {
            progressCallback?.Invoke("Preparing conversion request...", 10);
            var prompt = $"Convert this {fromDomain} project to {toDomain}. Analyze the provided code files and generate a complete {toDomain} project structure with all necessary files, including package.json, configuration files, main entry points, and proper directory structure. Provide the output as ---FILE: relative/path --- content for each file.\n{code}";
            progressCallback?.Invoke("Analyzing code structure...", 20);
            progressCallback?.Invoke("Sending request to Gemini AI service...", 30);
            var result = await GenerateTextAsync(prompt, progressCallback, 40, 80, customAiApiKey);
            progressCallback?.Invoke("Processing generated files...", 90);
            progressCallback?.Invoke("Conversion completed.", 100);
            return result;
        }

        public async Task<string> ConvertCodeChunksAsync(System.Collections.Generic.List<StartUply.Domain.Entities.ProjectChunk> chunks, string projectPath, string fromDomain, string toDomain, Action<string, int>? progressCallback = null, string? customAiApiKey = null)
        {
            var combinedOutput = new System.Text.StringBuilder();
            var previousContext = new System.Text.StringBuilder();

            int totalChunks = chunks.Count;
            int currentChunkIdx = 0;

            foreach(var chunk in chunks.OrderBy(c => c.Order))
            {
                currentChunkIdx++;
                
                var chunkCode = new System.Text.StringBuilder();
                foreach(var file in chunk.FilePaths)
                {
                     chunkCode.AppendLine($"---FILE: {System.IO.Path.GetRelativePath(projectPath, file)} ---");
                     chunkCode.AppendLine(System.IO.File.ReadAllText(file));
                }

                progressCallback?.Invoke($"Generating code for chunk {currentChunkIdx}/{totalChunks} ({chunk.Name})...", 20 + (60 * currentChunkIdx / totalChunks));
                
                var prompt = $"Convert this {fromDomain} project to {toDomain}. This is part {currentChunkIdx} of {totalChunks}. Focus on generating the {chunk.Name}. Provide the output as ---FILE: relative/path --- content for each file.\n\nContext from previously generated files:\n{previousContext}\n\nCode to convert:\n{chunkCode}";
                
                var result = await GenerateTextAsync(prompt, null, 0, 0, customAiApiKey);
                combinedOutput.AppendLine(result);
                
                previousContext.AppendLine($"Files generated so far in chunk {chunk.Name}:");
                var lines = result.Split('\n');
                foreach(var line in lines) 
                {
                    if (line.StartsWith("---FILE:")) previousContext.AppendLine(line);
                }
            }
            
            progressCallback?.Invoke("Conversion completed.", 90);
            return combinedOutput.ToString();
        }

        public async Task<string> GenerateBackendAsync(string frontendCode, string targetDomain, Action<string, int>? progressCallback = null, string? customAiApiKey = null)
        {
            progressCallback?.Invoke("Analyzing frontend code...", 10);
            var prompt = $"Analyze this frontend code and generate a {targetDomain} backend. Provide the output as a list of files with their paths and content, separated by ---FILE: relative/path ---.\n{frontendCode}";
            progressCallback?.Invoke("Preparing backend generation...", 20);
            progressCallback?.Invoke("Generating backend code with Gemini AI...", 30);
            var result = await GenerateTextAsync(prompt, progressCallback, 40, 80, customAiApiKey);
            progressCallback?.Invoke("Processing backend files...", 90);
            progressCallback?.Invoke("Backend generation completed.", 100);
            return result;
        }

        public async Task<string> GenerateBaseProjectAsync(string domain, Action<string, int>? progressCallback = null, string? customAiApiKey = null)
        {
            progressCallback?.Invoke("Preparing project generation...", 10);
            var prompt = $"Generate a basic project structure and starter files for a {domain} application. Provide the output as a list of files with their paths and content, separated by ---FILE: relative/path ---.\nFor example:\n---FILE: package.json ---\n{{\"name\": \"my-app\"}}\n---FILE: src/index.js ---\nconsole.log('Hello');";
            progressCallback?.Invoke("Analyzing requirements...", 20);
            progressCallback?.Invoke("Generating project files with Gemini AI...", 30);
            var result = await GenerateTextAsync(prompt, progressCallback, 40, 80, customAiApiKey);
            progressCallback?.Invoke("Processing project structure...", 90);
            progressCallback?.Invoke("Project generation completed.", 100);
            return result;
        }

        private async Task<string> GenerateTextAsync(string prompt, Action<string, int>? progressCallback = null, int minProgress = 50, int maxProgress = 80, string? customAiApiKey = null)
        {
            const int maxRetries = 3;
            int retryCount = 0;
            int delayMs = 2000;
            var effectiveKey = !string.IsNullOrWhiteSpace(customAiApiKey) ? customAiApiKey.Trim() : _apiKey;

            // Compute SHA-256 hash of payload prompt for cache validation & integrity
            string promptSha256 = SecurityUtils.ComputeSha256(prompt);
            if (!string.IsNullOrEmpty(promptSha256) && _sha256Cache.TryGetValue(promptSha256, out var cachedResponse))
            {
                progressCallback?.Invoke("Retrieved cached output (SHA-256 verified)...", maxProgress);
                return cachedResponse;
            }

            while (retryCount < maxRetries)
            {
                try
                {
                    progressCallback?.Invoke($"Sending request to Gemini AI...{(retryCount > 0 ? $" (retry {retryCount})" : "")}", minProgress);

                    var request = new
                    {
                        contents = new[]
                        {
                            new
                            {
                                parts = new[]
                                {
                                    new { text = prompt }
                                }
                            }
                        },
                        generationConfig = new
                        {
                            temperature = 0.2,
                            maxOutputTokens = 8192
                        }
                    };

                    var endpoint = $"models/{_modelId}:generateContent?key={effectiveKey}";
                    var response = await _httpClient.PostAsJsonAsync(endpoint, request);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorBody = await response.Content.ReadAsStringAsync();
                        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || 
                            errorBody.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) || 
                            errorBody.Contains("quota", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new GeminiRateLimitException("Gemini free tier rate limit reached. Please wait a moment or use your own Gemini API key (BYOK).");
                        }

                        response.EnsureSuccessStatusCode();
                    }

                    progressCallback?.Invoke("Processing Gemini AI response...", maxProgress);
                    var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();
                    var generatedText = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

                    if (!string.IsNullOrEmpty(generatedText) && !string.IsNullOrEmpty(promptSha256))
                    {
                        _sha256Cache[promptSha256] = generatedText;
                    }

                    return generatedText ?? "Error generating response from Gemini API";
                }
                catch (Exception ex)
                {
                    retryCount++;
                    bool isRateLimit = ex is GeminiRateLimitException || 
                                       (ex is HttpRequestException httpEx && httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests) ||
                                       ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase) || 
                                       ex.Message.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
                                       ex.Message.Contains("429");

                    if (retryCount >= maxRetries)
                    {
                        if (isRateLimit)
                        {
                            throw new GeminiRateLimitException("Gemini free tier rate limit reached. Please wait a moment or use your own Gemini API key (BYOK).", ex);
                        }
                        throw;
                    }
                    
                    if (isRateLimit)
                    {
                        delayMs = 15000 * retryCount; // 15s, 30s backoff
                        progressCallback?.Invoke($"Rate limit hit. Cooling down for {delayMs/1000}s...", minProgress);
                    }
                    else
                    {
                        progressCallback?.Invoke($"AI service warning, retrying in {delayMs/1000}s...", minProgress);
                    }
                    
                    await Task.Delay(delayMs);
                }
            }

            throw new Exception("Unexpected error in Gemini AI service");
        }
    }

    public class GeminiResponse
    {
        public GeminiCandidate[]? Candidates { get; set; }
    }

    public class GeminiCandidate
    {
        public GeminiContent? Content { get; set; }
    }

    public class GeminiContent
    {
        public GeminiPart[]? Parts { get; set; }
    }

    public class GeminiPart
    {
        public string? Text { get; set; }
    }
}