using Core.Entities;

namespace Core.Interfaces
{
    public interface ILabelRepository : IRepository<LabelClass>
    {
        Task<bool> ExistsInProjectAsync(int projectId, string labelName);
    }
}
