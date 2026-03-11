using BLL.Interfaces;
using Core.Constants;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Entities;
using DAL.Interfaces;

namespace BLL.Services
{
    public class DisputeService : IDisputeService
    {
        private readonly IDisputeRepository _disputeRepo;
        private readonly IAssignmentRepository _assignmentRepo;

        public DisputeService(IDisputeRepository disputeRepo, IAssignmentRepository assignmentRepo)
        {
            _disputeRepo = disputeRepo;
            _assignmentRepo = assignmentRepo;
        }

        public async Task CreateDisputeAsync(string annotatorId, CreateDisputeRequest request)
        {
            var assignment = await _assignmentRepo.GetByIdAsync(request.AssignmentId);
            if (assignment == null) throw new Exception("Task not found");

            if (assignment.AnnotatorId != annotatorId)
                throw new Exception("Unauthorized: You do not own this task.");
            if (assignment.Status != TaskStatusConstants.Rejected)
                throw new Exception("You can only dispute rejected tasks.");
            var existingDisputes = await _disputeRepo.GetDisputesByAnnotatorAsync(annotatorId);
            if (existingDisputes.Any(d => d.AssignmentId == request.AssignmentId && d.Status == "Pending"))
            {
                throw new Exception("A dispute for this task is already pending.");
            }

            var dispute = new Dispute
            {
                AssignmentId = request.AssignmentId,
                AnnotatorId = annotatorId,
                Reason = request.Reason,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            await _disputeRepo.AddAsync(dispute);
            await _disputeRepo.SaveChangesAsync();
        }

        public async Task ResolveDisputeAsync(ResolveDisputeRequest request)
        {
            var dispute = await _disputeRepo.GetDisputeWithDetailsAsync(request.DisputeId);
            if (dispute == null) throw new Exception("Dispute not found");

            if (dispute.Status != "Pending") throw new Exception("This dispute has already been resolved.");

            dispute.ManagerComment = request.ManagerComment;
            dispute.ResolvedAt = DateTime.UtcNow;

            if (request.IsAccepted)
            {
                dispute.Status = "Accepted";
                if (dispute.Assignment != null)
                {
                    dispute.Assignment.Status = TaskStatusConstants.Approved;
                }
            }
            else
            {
                dispute.Status = "Rejected";
            }

            await _disputeRepo.SaveChangesAsync();
        }

        public async Task<List<DisputeResponse>> GetDisputesAsync(int projectId, string userId, string role)
        {
            List<Dispute> disputes;

            if (role == UserRoles.Manager || role == UserRoles.Admin)
            {
                disputes = await _disputeRepo.GetDisputesByProjectAsync(projectId);
            }
            else
            {
                disputes = await _disputeRepo.GetDisputesByAnnotatorAsync(userId);
            }

            return disputes.Select(MapToResponse).ToList();
        }

        /// <summary>
        /// Maps a Dispute entity to a flat DisputeResponse DTO (no circular references).
        /// </summary>
        private static DisputeResponse MapToResponse(Dispute d)
        {
            return new DisputeResponse
            {
                Id = d.Id,
                AssignmentId = d.AssignmentId,
                AnnotatorId = d.AnnotatorId,
                AnnotatorName = d.Annotator?.FullName ?? d.Annotator?.Email,
                Reason = d.Reason,
                Status = d.Status,
                ManagerComment = d.ManagerComment,
                CreatedAt = d.CreatedAt,
                ResolvedAt = d.ResolvedAt,
                ProjectId = d.Assignment?.ProjectId,
                ProjectName = d.Assignment?.Project?.Name,
                DataItemUrl = d.Assignment?.DataItem?.StorageUrl,
                AssignmentStatus = d.Assignment?.Status,
                ReviewerName = d.Assignment?.Reviewer?.FullName ?? d.Assignment?.Reviewer?.Email,
            };
        }
    }
}
