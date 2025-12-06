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
            _apiKey = configuration["HuggingFace:ApiKey"];
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public async Task<string> ConvertCodeAsync(string code, string fromDomain, string toDomain)
        {
            var prompt = $"Convert this {fromDomain} code to {toDomain}. The input is structured as ---FILE: relative/path --- followed by content. Provide the output in the same format, with converted code for each file.\n{code}";
            return await GenerateTextAsync(prompt);
        }

        public async Task<string> GenerateBackendAsync(string frontendCode, string targetDomain)
        {
            var prompt = $"Analyze this frontend code and generate a {targetDomain} backend. Provide the output as a list of files with their paths and content, separated by ---FILE---.\n{frontendCode}";
            return await GenerateTextAsync(prompt);
        }

        public async Task<string> GenerateBaseProjectAsync(string domain)
        {
            var prompt = $"Generate a basic project structure and starter files for a {domain} application. Provide the output as a list of files with their paths and content, separated by ---FILE---.\nFor example:\n---FILE: package.json ---\n{{\"name\": \"my-app\"}}\n---FILE: src/index.js ---\nconsole.log('Hello');";
            return await GenerateTextAsync(prompt);
        }

        private async Task<string> GenerateTextAsync(string prompt)
        {
            var request = new { inputs = prompt, parameters = new { max_length = 500 } };
            var response = await _httpClient.PostAsJsonAsync(ModelId, request);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<HuggingFaceResponse[]>();
            return result?.FirstOrDefault()?.GeneratedText ?? "Error generating response";
        }
    }

    public class HuggingFaceResponse
    {
        public string GeneratedText { get; set; }
    }
}