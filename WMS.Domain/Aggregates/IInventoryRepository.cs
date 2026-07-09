using WMS.Domain.Aggregates;

namespace WMS.Domain.Aggregates;

/*
* 仓库接口
*/
public interface IInventoryRepository
{
    Task<Inventory?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task AddAsync(Inventory inventory, CancellationToken cancellationToken = default);
    Task UpdateAsync(Inventory inventory, CancellationToken cancellationToken = default);
}