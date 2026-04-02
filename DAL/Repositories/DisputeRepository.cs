using Core.Interfaces;
using Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Repositories
{
    public class DisputeRepository : Repository<Dispute>, IDisputeRepository
    {
        private ApplicationDbContext AppContext => (ApplicationDbContext)_context;

        public DisputeRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<List<Dispute>> GetDisputesByProjectAsync(int projectId)
        {
            return await AppContext.Disputes
                .Include(d => d.Annotator)
                .Include(d => d.Manager)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.Project)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.DataItem)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.Reviewer)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.Annotations)
                .Where(d => d.Assignment != null && d.Assignment.ProjectId == projectId)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Dispute>> GetDisputesByAnnotatorAsync(string annotatorId)
        {
            return await AppContext.Disputes
                .Include(d => d.Annotator)
                .Include(d => d.Manager)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.DataItem)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.Project)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.Reviewer)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.Annotations)
                .Where(d => d.AnnotatorId == annotatorId)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Dispute>> GetDisputesByReviewerAsync(string reviewerId, int projectId = 0)
        {
            var query = AppContext.Disputes
                .Include(d => d.Annotator)
                .Include(d => d.Manager)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.DataItem)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.Project)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.Reviewer)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.Annotations)
                .Where(d => d.Assignment != null && d.Assignment.ReviewerId == reviewerId);

            if (projectId > 0)
            {
                query = query.Where(d => d.Assignment != null && d.Assignment.ProjectId == projectId);
            }

            return await query
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();
        }

        public async Task<Dispute?> GetDisputeWithDetailsAsync(int disputeId)
        {
            return await AppContext.Disputes
                .Include(d => d.Annotator)
                .Include(d => d.Manager)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.DataItem)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.ReviewLogs)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.Annotations)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.Project)
                .Include(d => d.Assignment!)
                    .ThenInclude(a => a.Reviewer)
                .FirstOrDefaultAsync(d => d.Id == disputeId);
        }
    }
}

