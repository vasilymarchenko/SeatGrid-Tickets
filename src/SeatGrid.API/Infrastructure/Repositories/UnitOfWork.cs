using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Infrastructure.Persistence;
using System.Data;

namespace SeatGrid.API.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly SeatGridDbContext _context;
    private IDbContextTransaction? _currentTransaction;

    public UnitOfWork(SeatGridDbContext context)
    {
        _context = context;
    }

    public async Task BeginTransactionAsync(IsolationLevel isolationLevel)
    {
        if (_currentTransaction != null)
        {
            return;
        }
        _currentTransaction = await _context.Database.BeginTransactionAsync(isolationLevel);
    }

    public async Task CommitTransactionAsync()
    {
        try
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.CommitAsync();
            }
        }
        catch
        {
            await RollbackTransactionAsync();
            throw;
        }
        finally
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    public async Task RollbackTransactionAsync()
    {
        try
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.RollbackAsync();
            }
        }
        finally
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
