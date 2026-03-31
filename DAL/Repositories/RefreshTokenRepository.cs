using Core.Interfaces;
using Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Repositories
{
    public class RefreshTokenRepository : Repository<RefreshToken>, IRefreshTokenRepository
    {
        public RefreshTokenRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<RefreshToken?> GetByTokenAsync(string token)
        {
            return await _dbSet
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == token);
        }

        public async Task<RefreshToken?> GetActiveTokenByUserIdAsync(string userId)
        {
            return await _dbSet
                .FirstOrDefaultAsync(rt => rt.UserId == userId && rt.IsActive);
        }

        public async Task RevokeAllUserTokensAsync(string userId)
        {
            var tokens = await _dbSet
                .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
                .ToListAsync();

            foreach (var token in tokens)
            {
                token.RevokedAt = DateTime.UtcNow;
            }

            await SaveChangesAsync();
        }
    }
}

