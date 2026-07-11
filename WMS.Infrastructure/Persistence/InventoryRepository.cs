using Microsoft.EntityFrameworkCore;
using WMS.Domain.Aggregates;

namespace WMS.Infrastructure.Persistence;

public class InventoryRepository : IInventoryRepository
{
    private readonly WmsDbContext _dbContext;

    public InventoryRepository(WmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Inventory?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Inventories.FirstOrDefaultAsync(i => i.ProductId == productId, cancellationToken);
    }

    public async Task AddAsync(Inventory inventory, CancellationToken cancellationToken = default)
    {
        await _dbContext.Inventories.AddAsync(inventory, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateAsync(Inventory inventory, CancellationToken cancellationToken = default)
    {
        _dbContext.Inventories.Update(inventory);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}