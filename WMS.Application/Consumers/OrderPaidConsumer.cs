using MassTransit;
using Serilog;
using Shop.Events;
using WMS.Domain.Aggregates;

namespace WMS.Application.Consumers;

public class OrderPaidConsumer : IConsumer<OrderPaidEvent>
{
    private readonly IInventoryRepository _inventoryRepository;

    public OrderPaidConsumer(IInventoryRepository inventoryRepository)
    {
        _inventoryRepository = inventoryRepository;
    }

    public async Task Consume(ConsumeContext<OrderPaidEvent> context)
    {
        var evt = context.Message;

        Log.Information("收到订单支付事件，开始扣减库存: OrderId={OrderId}, ProductId={ProductId}, Quantity={Quantity}",
            evt.OrderId, evt.ProductId, evt.Quantity);

        var inventory = await _inventoryRepository.GetByProductIdAsync(evt.ProductId, context.CancellationToken);

        if (inventory is null)
        {
            Log.Warning("库存记录未找到，发布库存不足事件: ProductId={ProductId}", evt.ProductId);
            await context.Publish(new StockInsufficientEvent(
                evt.OrderId, evt.ProductId, evt.Quantity, DateTime.UtcNow));
            return;
        }

        try
        {
            inventory.Deduct(evt.Quantity);
            await _inventoryRepository.UpdateAsync(inventory, context.CancellationToken);
            await _inventoryRepository.SaveChangesAsync(context.CancellationToken);

            await context.Publish(new StockDeductedEvent(
                evt.OrderId, evt.ProductId, evt.Quantity, DateTime.UtcNow));

            Log.Information("库存扣减成功: OrderId={OrderId}, ProductId={ProductId}, Available={Available}",
                evt.OrderId, evt.ProductId, inventory.AvailableQuantity);

            if (inventory.AvailableQuantity < 100)
            {
                inventory.Replenish(500);
                await _inventoryRepository.UpdateAsync(inventory, context.CancellationToken);
                await _inventoryRepository.SaveChangesAsync(context.CancellationToken);
                Log.Information("库存不足 100，自动补充 500 件: ProductId={ProductId}, Available={Available}",
                    evt.ProductId, inventory.AvailableQuantity);
            }
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "库存不足，无法扣减: ProductId={ProductId}, Requested={Requested}, Available={Available}",
                evt.ProductId, evt.Quantity, inventory.AvailableQuantity);
            await context.Publish(new StockInsufficientEvent(
                evt.OrderId, evt.ProductId, evt.Quantity, DateTime.UtcNow));
        }
    }
}
