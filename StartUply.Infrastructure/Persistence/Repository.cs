using StartUply.Application.Interfaces;
using StartUply.Domain.Entities;

namespace StartUply.Infrastructure.Persistence
{
    public class Repository<T> : IRepository<T> where T : BaseEntity
    {
        // Placeholder implementation - in a real app, this would use EF Core or similar
        private readonly List<T> _entities = new();

        public Task<T?> GetByIdAsync(int id)
        {
            return Task.FromResult(_entities.FirstOrDefault(e => e.Id == id));
        }

        public Task<IEnumerable<T>> GetAllAsync()
        {
            return Task.FromResult<IEnumerable<T>>(_entities);
        }

        public Task AddAsync(T entity)
        {
            entity.Id = _entities.Count + 1;
            entity.CreatedAt = DateTime.UtcNow;
            _entities.Add(entity);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(T entity)
        {
            var existing = _entities.FirstOrDefault(e => e.Id == entity.Id);
            if (existing != null)
            {
                entity.UpdatedAt = DateTime.UtcNow;
                // In real implementation, update properties
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int id)
        {
            var entity = _entities.FirstOrDefault(e => e.Id == id);
            if (entity != null)
            {
                _entities.Remove(entity);
            }
            return Task.CompletedTask;
        }
    }
}