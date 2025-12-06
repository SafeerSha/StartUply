using StartUply.Application.Interfaces;
using System;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace StartUply.Infrastructure.Services
{
    public class AIService : IAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string ModelId = "google/gemini-2.0-flash-exp:free";

        public AIService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
            _apiKey = configuration["OpenRouter:ApiKey"] ?? throw new ArgumentNullException("OpenRouter:ApiKey is not configured");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public async Task<string> ConvertCodeAsync(string code, string fromDomain, string toDomain, Action<string, int>? progressCallback = null)
        {
            progressCallback?.Invoke("Preparing conversion request...", 10);
            var prompt = $"Convert this {fromDomain} project to {toDomain}. Analyze the provided code files and generate a complete {toDomain} project structure with all necessary files, including package.json, configuration files, main entry points, and proper directory structure. Provide the output as ---FILE: relative/path --- content for each file.\n{code}";
            progressCallback?.Invoke("Analyzing code structure...", 20);
            progressCallback?.Invoke("Sending request to AI service...", 30);
            var result = await GenerateTextAsync(prompt, progressCallback, 40, 80);
            progressCallback?.Invoke("Processing generated files...", 90);
            progressCallback?.Invoke("Conversion completed.", 100);
            return result;
        }

        public async Task<string> GenerateBackendAsync(string frontendCode, string targetDomain, Action<string, int>? progressCallback = null)
        {
            progressCallback?.Invoke("Analyzing frontend code...", 10);
            var prompt = $"Analyze this frontend code and generate a {targetDomain} backend. Provide the output as a list of files with their paths and content, separated by ---FILE---.\n{frontendCode}";
            progressCallback?.Invoke("Preparing backend generation...", 20);
            progressCallback?.Invoke("Generating backend code...", 30);
            var result = await GenerateTextAsync(prompt, progressCallback, 40, 80);
            progressCallback?.Invoke("Processing backend files...", 90);
            progressCallback?.Invoke("Backend generation completed.", 100);
            return result;
        }

        public async Task<string> GenerateBaseProjectAsync(string domain, Action<string, int>? progressCallback = null)
        {
            progressCallback?.Invoke("Preparing project generation...", 10);
            var prompt = $"Generate a basic project structure and starter files for a {domain} application. Provide the output as a list of files with their paths and content, separated by ---FILE---.\nFor example:\n---FILE: package.json ---\n{{\"name\": \"my-app\"}}\n---FILE: src/index.js ---\nconsole.log('Hello');";
            progressCallback?.Invoke("Analyzing requirements...", 20);
            progressCallback?.Invoke("Generating project files...", 30);
            var result = await GenerateTextAsync(prompt, progressCallback, 40, 80);
            progressCallback?.Invoke("Processing project structure...", 90);
            progressCallback?.Invoke("Project generation completed.", 100);
            return result;
        }

        private async Task<string> GenerateTextAsync(string prompt, Action<string, int>? progressCallback = null, int minProgress = 50, int maxProgress = 80)
        {
            const int maxRetries = 5;
            int retryCount = 0;
            int delayMs = 2000; // Start with 2 seconds

            while (retryCount < maxRetries)
            {
                try
                {
                    progressCallback?.Invoke($"Sending request to AI model...{(retryCount > 0 ? $" (retry {retryCount})" : "")}", minProgress);
                    var request = new
                    {
                        model = ModelId,
                        messages = new[]
                        {
                            new { role = "user", content = prompt }
                        },
                        max_tokens = 8192,
                        temperature = 0.2
                    };
                    var response = await _httpClient.PostAsJsonAsync("chat/completions", request);
                    response.EnsureSuccessStatusCode();
                    progressCallback?.Invoke("Processing AI response...", maxProgress);
                    var result = await response.Content.ReadFromJsonAsync<OpenRouterResponse>();
                    return result?.Choices?.FirstOrDefault()?.Message?.Content ?? "Error generating response";
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        throw new Exception("Rate limit exceeded after multiple retries. The free AI model has strict limits. Consider upgrading to a paid plan or waiting longer before retrying.", ex);
                    }
                    progressCallback?.Invoke($"Rate limit hit, waiting {delayMs}ms before retry {retryCount}/{maxRetries}...", minProgress);
                    await Task.Delay(delayMs);
                    delayMs = Math.Min(delayMs * 2, 30000); // Exponential backoff, max 30 seconds
                }
            }

            throw new Exception("Unexpected error in AI service");
        }
    }

    public class OpenRouterResponse
    {
        public Choice[]? Choices { get; set; }
    }

    public class Choice
    {
        public Message? Message { get; set; }
    }

    public class Message
    {
        public string? Content { get; set; }
    }
}