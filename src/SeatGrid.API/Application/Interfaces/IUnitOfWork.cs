using System.Data;

namespace SeatGrid.API.Application.Interfaces;

public interface IUnitOfWork
{
    Task BeginTransactionAsync(IsolationLevel isolationLevel);
    Task CommitTransactionAsync();
    Task RollbackTransactionAsync();
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
