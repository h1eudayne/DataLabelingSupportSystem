using Core.Entities;

namespace Core.Interfaces
{
    public interface IDisputeRepository : IRepository<Dispute>
    {
        Task<List<Dispute>> GetDisputesByProjectAsync(int projectId);
        Task<List<Dispute>> GetDisputesByAnnotatorAsync(string annotatorId);
        Task<Dispute?> GetDisputeWithDetailsAsync(int disputeId);
    }
}
