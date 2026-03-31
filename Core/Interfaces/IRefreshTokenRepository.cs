using Core.Entities;

namespace Core.Interfaces
{
    public interface IRefreshTokenRepository : IRepository<RefreshToken>
    {
        Task<RefreshToken?> GetByTokenAsync(string token);
        Task<RefreshToken?> GetActiveTokenByUserIdAsync(string userId);
        Task RevokeAllUserTokensAsync(string userId);
    }
}
