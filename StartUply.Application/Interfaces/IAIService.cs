namespace StartUply.Application.Interfaces
{
    public interface IAIService
    {
        Task<string> ConvertCodeAsync(string code, string fromDomain, string toDomain);
        Task<string> GenerateBackendAsync(string frontendCode, string targetDomain);
        Task<string> GenerateBaseProjectAsync(string domain);
    }
}