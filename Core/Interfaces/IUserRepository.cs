using Core.Entities;

namespace Core.Interfaces
{
    public interface IUserRepository : IRepository<User>
    {
        Task<User?> GetUserByEmailAsync(string email);
        Task<bool> IsEmailExistsAsync(string email);
        Task<bool> HasAdminRoleAsync();
    }
}
