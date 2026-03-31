using Core.Entities;

namespace Core.Interfaces
{
    public interface IDisputeRepository : IRepository<Dispute>
    {
        Task<List<Dispute>> GetDisputesByProjectAsync(int projectId);
        Task<List<Dispute>> GetDisputesByAnnotatorAsync(string annotatorId);
        Task<List<Dispute>> GetDisputesByReviewerAsync(string reviewerId, int projectId = 0);
        Task<Dispute?> GetDisputeWithDetailsAsync(int disputeId);
    }
}
