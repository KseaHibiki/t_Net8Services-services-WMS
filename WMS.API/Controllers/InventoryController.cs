using Microsoft.AspNetCore.Mvc;
using Serilog;
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
        Log.Information("查询库存: ProductId={ProductId}", productId);

        var inventory = await _inventoryRepository.GetByProductIdAsync(productId);
        if (inventory is null)
        {
            Log.Warning("库存记录未找到: ProductId={ProductId}", productId);
            return NotFound(new { Message = $"Inventory for product {productId} not found." });
        }

        Log.Information("库存查询结果: ProductId={ProductId}, Available={Available}, Reserved={Reserved}",
            productId, inventory.AvailableQuantity, inventory.ReservedQuantity);

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
        Log.Information("初始化库存: ProductId={ProductId}, Quantity={Quantity}",
            request.ProductId, request.Quantity);

        var existing = await _inventoryRepository.GetByProductIdAsync(request.ProductId);
        if (existing is not null)
        {
            Log.Warning("库存已存在，无法重复初始化: ProductId={ProductId}", request.ProductId);
            return Conflict(new { Message = $"Inventory for product {request.ProductId} already exists." });
        }

        var inventory = Inventory.Create(request.ProductId, request.Quantity);
        await _inventoryRepository.AddAsync(inventory);

        Log.Information("库存初始化成功: ProductId={ProductId}, Quantity={Quantity}",
            request.ProductId, request.Quantity);

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
