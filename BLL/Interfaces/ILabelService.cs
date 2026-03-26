using Core.DTOs.Requests;
using Core.DTOs.Responses;

namespace BLL.Interfaces
{
    public interface ILabelService
    {
        Task<LabelResponse> CreateLabelAsync(CreateLabelRequest request);
        Task<LabelResponse> UpdateLabelAsync(int labelId, UpdateLabelRequest request);
        Task<List<LabelResponse>> GetLabelsByProjectIdAsync(int projectId);
        Task DeleteLabelAsync(int labelId);
        Task<int> CheckLabelUsageAsync(int labelId);
    }
}