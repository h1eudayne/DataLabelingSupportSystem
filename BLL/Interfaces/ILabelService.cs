using Core.DTOs.Requests;
using Core.DTOs.Responses;

namespace BLL.Interfaces
{
    public interface ILabelService
    {
        Task<LabelResponse> CreateLabelAsync(string userId, CreateLabelRequest request);
        Task<LabelResponse> UpdateLabelAsync(string userId, int labelId, UpdateLabelRequest request);
        Task<List<LabelResponse>> GetLabelsByProjectIdAsync(int projectId);
        Task DeleteLabelAsync(string userId, int labelId);
        
        Task<LabelUsageResponse> CheckLabelUsageAsync(int labelId);
    }
}