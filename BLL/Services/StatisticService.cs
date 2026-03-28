using BLL.Interfaces;
using DAL.Interfaces;
using Core.Entities;
using Core.DTOs.Responses;
using Core.Constants;

namespace BLL.Services
{
    public class StatisticService : IStatisticService
    {
        private readonly IRepository<UserProjectStat> _statsRepo;
        private readonly IProjectRepository _projectRepo;
        private readonly IAssignmentRepository _assignmentRepo;
        private readonly IRepository<ReviewLog> _reviewLogRepo;
        private readonly IRepository<Dispute> _disputeRepo;

        public StatisticService(
            IRepository<UserProjectStat> statsRepo,
            IProjectRepository projectRepo,
            IAssignmentRepository assignmentRepo,
            IRepository<ReviewLog> reviewLogRepo,
            IRepository<Dispute> disputeRepo)
        {
            _statsRepo = statsRepo;
            _projectRepo = projectRepo;
            _assignmentRepo = assignmentRepo;
            _reviewLogRepo = reviewLogRepo;
            _disputeRepo = disputeRepo;
        }

        private async Task<UserProjectStat> GetOrCreateStatAsync(string userId, int projectId, bool isReviewer = false)
        {
            var allStats = await _statsRepo.GetAllAsync();
            var stat = allStats.FirstOrDefault(s => s.UserId == userId && s.ProjectId == projectId);

            if (stat == null)
            {
                stat = new UserProjectStat
                {
                    UserId = userId,
                    ProjectId = projectId,
                    Date = DateTime.UtcNow
                };

                if (!isReviewer)
                {
                    stat.EfficiencyScore = 100;
                    stat.AverageQualityScore = 100;
                }
                else
                {
                    stat.ReviewerQualityScore = 100;
                }

                await _statsRepo.AddAsync(stat);
            }
            return stat;
        }

        public async Task TrackNewAssignmentAsync(string annotatorId, int projectId, int count)
        {
            var stat = await GetOrCreateStatAsync(annotatorId, projectId);

            stat.TotalAssigned += count;

            if (stat.TotalAssigned > 0)
            {
                stat.EfficiencyScore = ((float)stat.TotalApproved / stat.TotalAssigned) * 100;
            }

            stat.Date = DateTime.UtcNow;
            await _statsRepo.SaveChangesAsync();
        }

        public async Task TrackReviewResultAsync(
              string annotatorId,
              string reviewerId,
              int projectId,
              bool isApproved,
              double taskScore,
              bool isCriticalError)
        {
            var annotatorStat = await GetOrCreateStatAsync(annotatorId, projectId);

            annotatorStat.TotalManagerDecisions++;
            if (isApproved)
            {
                annotatorStat.TotalApproved++;
                annotatorStat.TotalCorrectByManager++;
            }
            else
            {
                annotatorStat.TotalRejected++;
                if (isCriticalError) annotatorStat.TotalCriticalErrors++;
            }

            double totalScoreSoFar = (annotatorStat.AverageQualityScore * annotatorStat.TotalReviewedTasks) + taskScore;
            annotatorStat.TotalReviewedTasks++;
            annotatorStat.AverageQualityScore = Math.Round(totalScoreSoFar / annotatorStat.TotalReviewedTasks, 2);

            if (annotatorStat.TotalAssigned > 0)
            {
                annotatorStat.EfficiencyScore = ((float)annotatorStat.TotalApproved / annotatorStat.TotalAssigned) * 100;
            }
            annotatorStat.Date = DateTime.UtcNow;

            var reviewerStat = await GetOrCreateStatAsync(reviewerId, projectId, isReviewer: true);
            reviewerStat.TotalReviewsDone++;
            reviewerStat.Date = DateTime.UtcNow;

            await _statsRepo.SaveChangesAsync();
        }

        public async Task TrackAuditResultAsync(string reviewerId, int projectId, bool isCorrectDecision)
        {
            var reviewerStat = await GetOrCreateStatAsync(reviewerId, projectId, isReviewer: true);

            reviewerStat.TotalAuditedReviews++;

            if (isCorrectDecision)
            {
                reviewerStat.TotalCorrectDecisions++;
            }

            if (reviewerStat.TotalAuditedReviews > 0)
            {
                double accuracy = (double)reviewerStat.TotalCorrectDecisions / reviewerStat.TotalAuditedReviews;
                reviewerStat.ReviewerQualityScore = Math.Round(accuracy * 100, 2);
            }

            await _statsRepo.SaveChangesAsync();
        }

        public async Task TrackDisputeResolutionAsync(
            string annotatorId,
            List<(string reviewerId, bool wasCorrect)> reviewerResults,
            int projectId,
            bool annotatorWasCorrect)
        {
            var annotatorStat = await GetOrCreateStatAsync(annotatorId, projectId);
            annotatorStat.TotalManagerDecisions++;
            if (annotatorWasCorrect)
            {
                annotatorStat.TotalCorrectByManager++;
                annotatorStat.TotalApproved++;
            }
            else
            {
                annotatorStat.TotalRejected++;
            }
            annotatorStat.Date = DateTime.UtcNow;

            foreach (var (reviewerId, wasCorrect) in reviewerResults)
            {
                var reviewerStat = await GetOrCreateStatAsync(reviewerId, projectId, isReviewer: true);
                reviewerStat.TotalReviewerManagerDecisions++;
                if (wasCorrect)
                {
                    reviewerStat.TotalReviewerCorrectByManager++;
                }
                reviewerStat.Date = DateTime.UtcNow;
            }

            await _statsRepo.SaveChangesAsync();
        }

        public async Task TrackFirstPassCorrectAsync(string annotatorId, string reviewerId, int projectId)
        {
            var annotatorStat = await GetOrCreateStatAsync(annotatorId, projectId);
            annotatorStat.TotalFirstPassCorrect++;
            annotatorStat.Date = DateTime.UtcNow;

            var reviewerStat = await GetOrCreateStatAsync(reviewerId, projectId, isReviewer: true);
            reviewerStat.TotalReviewerManagerDecisions++;
            reviewerStat.TotalReviewerCorrectByManager++;
            reviewerStat.Date = DateTime.UtcNow;

            await _statsRepo.SaveChangesAsync();
        }

        public async Task TrackOverrideCountAsync(string reviewerId, int projectId)
        {
            var reviewerStat = await GetOrCreateStatAsync(reviewerId, projectId, isReviewer: true);
            reviewerStat.OverrideCount++;
            reviewerStat.Date = DateTime.UtcNow;
            await _statsRepo.SaveChangesAsync();
        }

        public async Task TrackDisputeCountAsync(string reviewerId, int projectId)
        {
            var reviewerStat = await GetOrCreateStatAsync(reviewerId, projectId, isReviewer: true);
            reviewerStat.DisputeCount++;
            reviewerStat.Date = DateTime.UtcNow;
            await _statsRepo.SaveChangesAsync();
        }

        public async Task<ReviewerStatsResponse> GetReviewerStatsAsync(string reviewerId)
        {
            var allStats = await _statsRepo.GetAllAsync();
            var reviewerStats = allStats.Where(s => s.UserId == reviewerId).ToList();

            var allReviewLogs = await _reviewLogRepo.GetAllAsync();
            var reviewerLogs = allReviewLogs.Where(rl => rl.ReviewerId == reviewerId).ToList();

            var allDisputes = await _disputeRepo.GetAllAsync();
            var resolvedDisputes = allDisputes.Where(d =>
                d.Status == "Accepted" || d.Status == "Rejected");

            var reviewerDisputes = new List<Dispute>();
            foreach (var dispute in resolvedDisputes)
            {
                var assignment = await _assignmentRepo.GetByIdAsync(dispute.AssignmentId);
                if (assignment != null && assignment.ReviewerId == reviewerId)
                {
                    reviewerDisputes.Add(dispute);
                }
            }

            int totalReviews = reviewerLogs.Count;
            int totalApproved = reviewerLogs.Count(l => l.Verdict == "Approved" || l.Verdict == "Approve");
            int totalRejected = reviewerLogs.Count(l => l.Verdict == "Rejected" || l.Verdict == "Reject");
            int totalOverridden = reviewerDisputes.Count;

            int totalDisputes = reviewerStats.Sum(s => s.DisputeCount);
            double disputeRate = totalReviews > 0 ? Math.Round((double)totalDisputes / totalReviews * 100, 2) : 0;

            double approvalRate = totalReviews > 0 ? Math.Round((double)totalApproved / totalReviews * 100, 2) : 0;
            double rejectionRate = totalReviews > 0 ? Math.Round((double)totalRejected / totalReviews * 100, 2) : 0;
            double overrideRate = totalReviews > 0 ? Math.Round((double)totalOverridden / totalReviews * 100, 2) : 0;

            int totalAudited = reviewerStats.Sum(s => s.TotalAuditedReviews);
            int correctDecisions = reviewerStats.Sum(s => s.TotalCorrectDecisions);
            double auditAccuracy = totalAudited > 0 ? Math.Round((double)correctDecisions / totalAudited * 100, 2) : 0;
            double kqsScore = reviewerStats.Count > 0 ? reviewerStats.Average(s => s.ReviewerQualityScore) : 100;

            var projectIds = reviewerStats.Select(s => s.ProjectId).Distinct().ToList();
            var projectSummaries = new List<ProjectReviewSummary>();

            foreach (var projectId in projectIds)
            {
                var project = await _projectRepo.GetByIdAsync(projectId);
                var stat = reviewerStats.FirstOrDefault(s => s.ProjectId == projectId);
                var logsForProject = reviewerLogs.Where(l =>
                {
                    var assignment = _assignmentRepo.GetByIdAsync(l.AssignmentId).Result;
                    return assignment != null && assignment.ProjectId == projectId;
                }).ToList();

                int projectApproved = logsForProject.Count(l => l.Verdict == "Approved" || l.Verdict == "Approve");
                int projectRejected = logsForProject.Count(l => l.Verdict == "Rejected" || l.Verdict == "Reject");
                int projectTotal = logsForProject.Count;

                projectSummaries.Add(new ProjectReviewSummary
                {
                    ProjectId = projectId,
                    ProjectName = project?.Name ?? "Unknown Project",
                    TotalReviews = projectTotal,
                    Approved = projectApproved,
                    Rejected = projectRejected,
                    ApprovalRate = projectTotal > 0 ? Math.Round((double)projectApproved / projectTotal * 100, 2) : 0,
                    KQSScore = stat?.ReviewerQualityScore ?? 100
                });
            }

            return new ReviewerStatsResponse
            {
                ReviewerId = reviewerId,
                TotalReviews = totalReviews,
                TotalApproved = totalApproved,
                TotalRejected = totalRejected,
                TotalOverridden = totalOverridden,
                TotalDisputes = totalDisputes,
                ApprovalRate = approvalRate,
                RejectionRate = rejectionRate,
                OverrideRate = overrideRate,
                DisputeRate = disputeRate,
                KQSScore = kqsScore,
                TotalAuditedReviews = totalAudited,
                CorrectDecisions = correctDecisions,
                AuditAccuracy = auditAccuracy,
                LastUpdated = DateTime.UtcNow,
                ProjectSummaries = projectSummaries
            };
        }

        public async Task DeductReliabilityScoreForOverdueTasksAsync()
        {
            var allAssignments = await _assignmentRepo.GetAllAsync();
            var overdueAssignments = allAssignments.Where(a =>
                a.Status == TaskStatusConstants.Assigned ||
                a.Status == TaskStatusConstants.InProgress ||
                a.Status == TaskStatusConstants.Submitted)
                .Where(a => a.Project != null && a.Project.Deadline < DateTime.UtcNow)
                .ToList();

            var allStats = await _statsRepo.GetAllAsync();

            foreach (var assignment in overdueAssignments)
            {
                var daysOverdue = (DateTime.UtcNow - assignment.Project.Deadline).Days;
                int deductionAmount = Math.Min(daysOverdue * 2, 20);

                var stat = allStats.FirstOrDefault(s => s.UserId == assignment.AnnotatorId && s.ProjectId == assignment.ProjectId);
                if (stat != null && !stat.IsLocked)
                {
                    stat.EfficiencyScore = Math.Max(0, stat.EfficiencyScore - deductionAmount);
                    stat.Date = DateTime.UtcNow;
                }
            }

            await _statsRepo.SaveChangesAsync();
        }
    }
}
