using StartUply.Application.Interfaces;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;

namespace StartUply.Infrastructure.Services
{
    public class AIService : IAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string ModelId = "bigcode/starcoderbase"; // Example model, adjust as needed

        public AIService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri("https://api-inference.huggingface.co/models/");
            _apiKey = configuration["HuggingFace:ApiKey"] ?? throw new ArgumentNullException("HuggingFace:ApiKey is not configured");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public async Task<string> ConvertCodeAsync(string code, string fromDomain, string toDomain, Action<string, int>? progressCallback = null)
        {
            progressCallback?.Invoke("Preparing conversion request...", 10);
            var prompt = $"Convert this {fromDomain} code to {toDomain}. The input is structured as ---FILE: relative/path --- followed by content. Provide the output in the same format, with converted code for each file.\n{code}";
            progressCallback?.Invoke("Sending request to AI service...", 30);
            var result = await GenerateTextAsync(prompt, progressCallback);
            progressCallback?.Invoke("Conversion completed.", 100);
            return result;
        }

        public async Task<string> GenerateBackendAsync(string frontendCode, string targetDomain, Action<string, int>? progressCallback = null)
        {
            progressCallback?.Invoke("Analyzing frontend code...", 10);
            var prompt = $"Analyze this frontend code and generate a {targetDomain} backend. Provide the output as a list of files with their paths and content, separated by ---FILE---.\n{frontendCode}";
            progressCallback?.Invoke("Generating backend code...", 30);
            var result = await GenerateTextAsync(prompt, progressCallback);
            progressCallback?.Invoke("Backend generation completed.", 100);
            return result;
        }

        public async Task<string> GenerateBaseProjectAsync(string domain, Action<string, int>? progressCallback = null)
        {
            progressCallback?.Invoke("Preparing project generation...", 10);
            var prompt = $"Generate a basic project structure and starter files for a {domain} application. Provide the output as a list of files with their paths and content, separated by ---FILE---.\nFor example:\n---FILE: package.json ---\n{{\"name\": \"my-app\"}}\n---FILE: src/index.js ---\nconsole.log('Hello');";
            progressCallback?.Invoke("Generating project files...", 30);
            var result = await GenerateTextAsync(prompt, progressCallback);
            progressCallback?.Invoke("Project generation completed.", 100);
            return result;
        }

        private async Task<string> GenerateTextAsync(string prompt, Action<string, int>? progressCallback = null)
        {
            progressCallback?.Invoke("Sending request to AI model...", 50);
            var request = new { inputs = prompt, parameters = new { max_length = 500 } };
            var response = await _httpClient.PostAsJsonAsync(ModelId, request);
            response.EnsureSuccessStatusCode();
            progressCallback?.Invoke("Processing AI response...", 80);
            var result = await response.Content.ReadFromJsonAsync<HuggingFaceResponse[]>();
            return result?.FirstOrDefault()?.GeneratedText ?? "Error generating response";
        }
    }

    public class HuggingFaceResponse
    {
        public string? GeneratedText { get; set; }
    }
}