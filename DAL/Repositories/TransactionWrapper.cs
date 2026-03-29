using Core.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace DAL.Repositories
{
    internal class TransactionWrapper : ITransaction
    {
        private readonly IDbContextTransaction _transaction;

        public TransactionWrapper(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
            => _transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => _transaction.RollbackAsync(cancellationToken);

        public void Dispose() => _transaction.Dispose();

        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }
}
