using Core.Interfaces;
using Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Repositories
{
    public class UserRepository : Repository<User>, IUserRepository
    {
        public UserRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _dbSet
                .Where(u => u.Email == email)
                .OrderByDescending(u => u.IsActive)
                .ThenByDescending(u => u.LastActivityAt)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> IsEmailExistsAsync(string email)
        {
            return await _dbSet.AnyAsync(u => u.Email == email);
        }

        public async Task<bool> HasAdminRoleAsync()
        {
            return await _dbSet.AnyAsync(u => u.Role == "Admin" && u.IsActive);
        }
    }
}

