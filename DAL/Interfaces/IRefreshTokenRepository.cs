using Core.Entities;

namespace DAL.Interfaces
{
    public interface IRefreshTokenRepository : IRepository<RefreshToken>
    {
        Task<RefreshToken?> GetByTokenAsync(string token);
        Task<RefreshToken?> GetActiveTokenByUserIdAsync(string userId);
        Task RevokeAllUserTokensAsync(string userId);
    }
}
