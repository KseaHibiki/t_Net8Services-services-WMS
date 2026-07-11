using Serilog;
using StackExchange.Redis;

namespace WMS.Application.Redis;

/// <summary>
/// Redis 库存预扣减服务
/// 使用 Lua 脚本保证原子性，key 格式: stock:available:{productId} / stock:reserved:{productId}
/// </summary>
public interface IRedisInventoryService
{
    /// <summary>初始化 Redis 库存（由 SeedInventory 调用）</summary>
    Task<bool> InitializeStockAsync(Guid productId, int quantity, CancellationToken ct = default);

    /// <summary>原子预扣减库存，返回 (成功, 剩余可用量)</summary>
    Task<(bool Success, int Remaining)> DeductStockAsync(Guid productId, int quantity, CancellationToken ct = default);

    /// <summary>回滚预扣减（下单失败时归还库存）</summary>
    Task RefundStockAsync(Guid productId, int quantity, CancellationToken ct = default);

    /// <summary>获取 Redis 库存信息</summary>
    Task<(int Available, int Reserved)?> GetStockAsync(Guid productId, CancellationToken ct = default);

    /// <summary>DB 扣减完成后同步更新 Redis 库存</summary>
    Task SyncFromDbAsync(Guid productId, int availableQuantity, int reservedQuantity, CancellationToken ct = default);
}

public class RedisInventoryService : IRedisInventoryService
{
    private readonly IDatabase _redis;

    private const string AvailableKeyPrefix = "stock:available:";
    private const string ReservedKeyPrefix = "stock:reserved:";

    /// <summary>
    /// Lua 脚本：原子预扣减库存
    /// KEYS[1] = stock:available:{productId}
    /// KEYS[2] = stock:reserved:{productId}
    /// ARGV[1] = 扣减数量
    /// 返回: 剩余可用量（>=0 成功），-1 库存不足
    /// </summary>
    private const string DeductLuaScript = @"
        local available = tonumber(redis.call('GET', KEYS[1]))
        if not available then return -2 end
        local qty = tonumber(ARGV[1])
        if available < qty then return -1 end
        redis.call('DECRBY', KEYS[1], qty)
        redis.call('INCRBY', KEYS[2], qty)
        return available - qty
    ";

    /// <summary>
    /// Lua 脚本：回滚预扣减
    /// KEYS[1] = stock:available:{productId}
    /// KEYS[2] = stock:reserved:{productId}
    /// ARGV[1] = 回滚数量
    /// </summary>
    private const string RefundLuaScript = @"
        local reserved = tonumber(redis.call('GET', KEYS[2]))
        if not reserved then return -1 end
        local qty = tonumber(ARGV[1])
        if reserved < qty then return -1 end
        redis.call('DECRBY', KEYS[2], qty)
        redis.call('INCRBY', KEYS[1], qty)
        return 0
    ";

    public RedisInventoryService(IConnectionMultiplexer redis)
    {
        _redis = redis.GetDatabase();
    }

    public async Task<bool> InitializeStockAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        var availKey = AvailableKeyPrefix + productId;
        var reservedKey = ReservedKeyPrefix + productId;

        var tran = _redis.CreateTransaction();
        _ = tran.StringSetAsync(availKey, quantity, when: When.NotExists);
        _ = tran.StringSetAsync(reservedKey, 0, when: When.NotExists);
        var committed = await tran.ExecuteAsync();

        if (committed)
            Log.Information("Redis 库存初始化成功: ProductId={ProductId}, Quantity={Quantity}", productId, quantity);
        else
            Log.Warning("Redis 库存已存在，跳过初始化: ProductId={ProductId}", productId);

        return committed;
    }

    public async Task<(bool Success, int Remaining)> DeductStockAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        var availKey = AvailableKeyPrefix + productId;
        var reservedKey = ReservedKeyPrefix + productId;

        var result = (int)await _redis.ScriptEvaluateAsync(
            DeductLuaScript,
            new RedisKey[] { availKey, reservedKey },
            new RedisValue[] { quantity });

        return result switch
        {
            -2 => (false, 0),  // 库存 key 不存在
            -1 => (false, 0),  // 库存不足
            _ => (true, result) // 成功，返回剩余
        };
    }

    public async Task RefundStockAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        var availKey = AvailableKeyPrefix + productId;
        var reservedKey = ReservedKeyPrefix + productId;

        var result = (int)await _redis.ScriptEvaluateAsync(
            RefundLuaScript,
            new RedisKey[] { availKey, reservedKey },
            new RedisValue[] { quantity });

        if (result == 0)
            Log.Information("Redis 库存回滚成功: ProductId={ProductId}, Quantity={Quantity}", productId, quantity);
        else
            Log.Warning("Redis 库存回滚失败: ProductId={ProductId}, Quantity={Quantity}", productId, quantity);
    }

    public async Task<(int Available, int Reserved)?> GetStockAsync(Guid productId, CancellationToken ct = default)
    {
        var availKey = AvailableKeyPrefix + productId;
        var reservedKey = ReservedKeyPrefix + productId;

        var values = await _redis.StringGetAsync(new RedisKey[] { availKey, reservedKey });

        if (!values[0].HasValue)
            return null;

        return ((int)values[0], values[1].HasValue ? (int)values[1] : 0);
    }

    public async Task SyncFromDbAsync(Guid productId, int availableQuantity, int reservedQuantity, CancellationToken ct = default)
    {
        var availKey = AvailableKeyPrefix + productId;
        var reservedKey = ReservedKeyPrefix + productId;

        var tran = _redis.CreateTransaction();
        _ = tran.StringSetAsync(availKey, availableQuantity);
        _ = tran.StringSetAsync(reservedKey, reservedQuantity);
        await tran.ExecuteAsync();

        Log.Information("Redis 库存已从 DB 同步: ProductId={ProductId}, Available={Available}, Reserved={Reserved}",
            productId, availableQuantity, reservedQuantity);
    }
}