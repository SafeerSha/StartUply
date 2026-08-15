using System.Collections.Generic;
using StartUply.Domain.Entities;

namespace StartUply.Application.Interfaces
{
    public interface IProjectChunkerService
    {
        /// <summary>
        /// Analyzes a directory, identifies dependencies, and groups files into ordered chunks.
        /// Chunks with lower Order values should be processed first.
        /// </summary>
        List<ProjectChunk> ChunkProject(string projectPath, string languageOrFramework);
    }
}
