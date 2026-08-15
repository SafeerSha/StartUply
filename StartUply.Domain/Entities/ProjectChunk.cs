using System;
using System.Collections.Generic;

namespace StartUply.Domain.Entities
{
    public class ProjectChunk
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int Order { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<string> FilePaths { get; set; } = new();
        public List<string> DependencyChunkIds { get; set; } = new();
    }
}
