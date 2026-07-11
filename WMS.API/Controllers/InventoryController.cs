using Microsoft.AspNetCore.Mvc;
using Serilog;
using WMS.Domain.Aggregates;
using StackExchange.Redis;
using System.Text.Json;

namespace WMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IDatabase _redis;
    private readonly IConfiguration _configuration;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public InventoryController(
        IInventoryRepository inventoryRepository,
        IConnectionMultiplexer redis,
        IConfiguration configuration)
    {
        _inventoryRepository = inventoryRepository;
        _redis = redis.GetDatabase();
        _configuration = configuration;
    }

    [HttpGet("{productId:guid}")]
    public async Task<IActionResult> GetInventory(Guid productId)
    {
        Log.Information("查询库存: ProductId={ProductId}", productId);

        var cacheKey = $"inventory:{productId}";
        var ttl = _configuration.GetValue<int>("RedisCache:InventoryTtlSeconds", 30);

        // 尝试从 Redis 缓存读取
        var cached = await _redis.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            Log.Information("库存缓存命中: ProductId={ProductId}", productId);
            return Ok(System.Text.Json.JsonSerializer.Deserialize<object>(cached!));
        }

        // 缓存未命中，查数据库
        var inventory = await _inventoryRepository.GetByProductIdAsync(productId);
        if (inventory is null)
        {
            Log.Warning("库存记录未找到: ProductId={ProductId}", productId);
            return NotFound(new { Message = $"Inventory for product {productId} not found." });
        }

        var result = new
        {
            inventory.Id,
            inventory.ProductId,
            inventory.AvailableQuantity,
            inventory.ReservedQuantity
        };

        // 写入缓存
        var json = System.Text.Json.JsonSerializer.Serialize(result, JsonOptions);
        await _redis.StringSetAsync(cacheKey, json, TimeSpan.FromSeconds(ttl));

        Log.Information("库存查询结果: ProductId={ProductId}, Available={Available}, Reserved={Reserved}",
            productId, inventory.AvailableQuantity, inventory.ReservedQuantity);

        return Ok(result);
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