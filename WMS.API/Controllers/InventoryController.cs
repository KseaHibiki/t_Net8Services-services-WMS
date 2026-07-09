using Microsoft.AspNetCore.Mvc;
using WMS.Domain.Aggregates;

namespace WMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryRepository _inventoryRepository;

    public InventoryController(IInventoryRepository inventoryRepository)
    {
        _inventoryRepository = inventoryRepository;
    }

    [HttpGet("{productId:guid}")]
    public async Task<IActionResult> GetInventory(Guid productId)
    {
        var inventory = await _inventoryRepository.GetByProductIdAsync(productId);
        if (inventory is null)
            return NotFound(new { Message = $"Inventory for product {productId} not found." });

        return Ok(new
        {
            inventory.Id,
            inventory.ProductId,
            inventory.AvailableQuantity,
            inventory.ReservedQuantity
        });
    }

    [HttpPost("seed")]
    public async Task<IActionResult> SeedInventory([FromBody] SeedInventoryRequest request)
    {
        var existing = await _inventoryRepository.GetByProductIdAsync(request.ProductId);
        if (existing is not null)
            return Conflict(new { Message = $"Inventory for product {request.ProductId} already exists." });

        var inventory = Inventory.Create(request.ProductId, request.Quantity);
        await _inventoryRepository.AddAsync(inventory);

        return CreatedAtAction(nameof(GetInventory), new { productId = inventory.ProductId }, new
        {
            inventory.Id,
            inventory.ProductId,
            inventory.AvailableQuantity,
            inventory.ReservedQuantity
        });
    }
}

public record SeedInventoryRequest(Guid ProductId, int Quantity);