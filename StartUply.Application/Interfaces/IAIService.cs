namespace StartUply.Application.Interfaces
{
    public interface IAIService
    {
        Task<string> ConvertCodeAsync(string code, string fromDomain, string toDomain, Action<string, int>? progressCallback = null);
        Task<string> GenerateBackendAsync(string frontendCode, string targetDomain, Action<string, int>? progressCallback = null);
        Task<string> GenerateBaseProjectAsync(string domain, Action<string, int>? progressCallback = null);
    }
}