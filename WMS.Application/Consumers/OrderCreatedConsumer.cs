using MassTransit;
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
        var eventMessage = context.Message;

        Console.WriteLine($"[WMS] Processing order {eventMessage.OrderId}, product {eventMessage.ProductId}, qty {eventMessage.Quantity}");

        var inventory = await _inventoryRepository.GetByProductIdAsync(eventMessage.ProductId, context.CancellationToken);

        if (inventory is null)
        {
            Console.WriteLine($"[WMS] Inventory not found for product {eventMessage.ProductId}. Publishing StockInsufficientEvent.");
            await _publishEndpoint.Publish(new StockInsufficientEvent(
                eventMessage.OrderId,
                eventMessage.ProductId,
                eventMessage.Quantity,
                DateTime.UtcNow), context.CancellationToken);
            return;
        }

        try
        {
            inventory.Deduct(eventMessage.Quantity);
            await _inventoryRepository.UpdateAsync(inventory, context.CancellationToken);

            await _publishEndpoint.Publish(new StockDeductedEvent(
                eventMessage.OrderId,
                eventMessage.ProductId,
                eventMessage.Quantity,
                DateTime.UtcNow), context.CancellationToken);

            Console.WriteLine($"[WMS] Stock deducted for order {eventMessage.OrderId}. Available: {inventory.AvailableQuantity}");
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine($"[WMS] Insufficient stock for product {eventMessage.ProductId}. Publishing StockInsufficientEvent.");
            await _publishEndpoint.Publish(new StockInsufficientEvent(
                eventMessage.OrderId,
                eventMessage.ProductId,
                eventMessage.Quantity,
                DateTime.UtcNow), context.CancellationToken);
        }
    }
}