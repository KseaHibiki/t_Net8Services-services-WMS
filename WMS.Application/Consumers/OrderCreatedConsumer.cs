using MassTransit;
using Serilog;
using Shop.Events;
using WMS.Domain.Aggregates;

namespace WMS.Application.Consumers;

public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IPublishEndpoint _publishEndpoint;

    public OrderCreatedConsumer(IInventoryRepository inventoryRepository, IPublishEndpoint publishEndpoint)
    {
        _inventoryRepository = inventoryRepository;
        _publishEndpoint = publishEndpoint;
    }

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var evt = context.Message;

        Log.Information("收到订单创建事件: OrderId={OrderId}, ProductId={ProductId}, Quantity={Quantity}",
            evt.OrderId, evt.ProductId, evt.Quantity);

        var inventory = await _inventoryRepository.GetByProductIdAsync(evt.ProductId, context.CancellationToken);

        if (inventory is null)
        {
            Log.Warning("库存记录未找到，发布库存不足事件: ProductId={ProductId}", evt.ProductId);
            await _publishEndpoint.Publish(new StockInsufficientEvent(
                evt.OrderId, evt.ProductId, evt.Quantity, DateTime.UtcNow), context.CancellationToken);
            return;
        }

        try
        {
            inventory.Deduct(evt.Quantity);
            await _inventoryRepository.UpdateAsync(inventory, context.CancellationToken);

            await _publishEndpoint.Publish(new StockDeductedEvent(
                evt.OrderId, evt.ProductId, evt.Quantity, DateTime.UtcNow), context.CancellationToken);

            Log.Information("库存扣减成功: OrderId={OrderId}, ProductId={ProductId}, Available={Available}",
                evt.OrderId, evt.ProductId, inventory.AvailableQuantity);

            if (inventory.AvailableQuantity < 100)
            {
                inventory.Replenish(500);
                await _inventoryRepository.UpdateAsync(inventory, context.CancellationToken);
                Log.Information("库存不足 100，自动补充 500 件: ProductId={ProductId}, Available={Available}",
                    evt.ProductId, inventory.AvailableQuantity);
            }
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "库存不足，无法扣减: ProductId={ProductId}, Requested={Requested}, Available={Available}",
                evt.ProductId, evt.Quantity, inventory.AvailableQuantity);
            await _publishEndpoint.Publish(new StockInsufficientEvent(
                evt.OrderId, evt.ProductId, evt.Quantity, DateTime.UtcNow), context.CancellationToken);
        }
    }
}
