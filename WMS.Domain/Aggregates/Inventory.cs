namespace WMS.Domain.Aggregates;
/*
* 仓库
* 2026年7月10日
*/
public class Inventory
{
    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public int AvailableQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }

    private Inventory() { }

    public static Inventory Create(Guid productId, int quantity)
    {
        if (quantity < 0)
            throw new ArgumentException("Quantity cannot be negative.", nameof(quantity));

        return new Inventory
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            AvailableQuantity = quantity,
            ReservedQuantity = 0
        };
    }

    public void Deduct(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than 0.", nameof(quantity));
        if (AvailableQuantity < quantity)
            throw new InvalidOperationException($"Insufficient stock. Available: {AvailableQuantity}, Requested: {quantity}");

        AvailableQuantity -= quantity;
        ReservedQuantity += quantity;
    }

    public void ConfirmDeduction(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than 0.", nameof(quantity));
        if (ReservedQuantity < quantity)
            throw new InvalidOperationException($"Cannot confirm deduction. Reserved: {ReservedQuantity}, Requested: {quantity}");

        ReservedQuantity -= quantity;
    }

    public void Replenish(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than 0.", nameof(quantity));

        AvailableQuantity += quantity;
    }
}