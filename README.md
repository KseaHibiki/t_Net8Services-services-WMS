# WMS 库存微服务

**Warehouse Management System** — 基于 .NET 8 构建的库存管理微服务，采用 DDD 四层架构，负责库存初始化、Redis 实时库存同步和支付后库存扣减。

## 架构

| 层 | 项目 | 职责 |
|---|---|---|
| Domain | `WMS.Domain` | 聚合根 `Inventory`（available + reserved 两阶段模型）、`IInventoryRepository` 接口 |
| Application | `WMS.Application` | `OrderPaidConsumer`（支付后扣库存）、`RedisInventoryService`（Redis 库存管理） |
| Infrastructure | `WMS.Infrastructure` | EF Core `WmsDbContext`、`InventoryRepository` 实现 |
| API | `WMS.API` | `InventoryController`、DI 注册、Swagger、启动入口 |

## 技术栈

| 组件 | 技术 |
|------|------|
| 运行时 | .NET 8 (ASP.NET Core) |
| 数据库 | MySQL 8.0 (via Pomelo.EntityFrameworkCore) |
| 消息队列 | MassTransit 8.3.0 + RabbitMQ |
| 缓存 | StackExchange.Redis 2.8.16 |
| 日志 | Serilog (控制台 + 按天滚动文件) |
| API 文档 | Swagger / Swashbuckle |
| 部署 | Docker |

## 核心机制

### 两阶段库存模型

库存采用 **available（可用）+ reserved（预占）** 两阶段模式：

- `Deduct(quantity)` — 预占库存（可用 → 预占）
- `ConfirmDeduction(quantity)` — 确认扣减（清除预占）
- `Replenish(quantity)` — 补货（增加可用）

### Redis 库存管理

`RedisInventoryService` 提供 Redis 侧的库存操作，与 Shop 侧 `RedisStockClient` 共享同一套 Lua 脚本和 Key 格式：

| 方法 | 说明 |
|------|------|
| `InitializeStockAsync` | Redis 事务 SET NX 初始化 available + reserved |
| `DeductStockAsync` | Lua 脚本原子预扣减（与 Shop 侧共享同一脚本） |
| `RefundStockAsync` | Lua 脚本原子回滚 |
| `GetStockAsync` | 批量 GET available + reserved |
| `SyncFromDbAsync` | DB 扣减完成后事务 SET 同步回 Redis（最终一致性） |

### Redis 查询缓存

- Key: `inventory:{productId}`，TTL: 30 秒
- 缓存命中直接返回，未命中查 DB 后写入缓存

### 自动补货

订单支付扣减库存后，若 `AvailableQuantity < 100`，系统自动补货 500 件（测试功能）。

## 领域模型

### Inventory（库存聚合根）

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | Guid | 主键 |
| ProductId | Guid | 商品 ID（唯一索引） |
| AvailableQuantity | int | 可用库存 |
| ReservedQuantity | int | 已预占库存 |

**业务规则**:
- 可用库存不能为负
- `Deduct` 时可用不足抛 `InvalidOperationException`
- 数量参数 <= 0 抛 `ArgumentException`

## API 接口

| 方法 | 路径 | 说明 | 响应 |
|------|------|------|------|
| POST | `/api/inventory/seed` | 初始化库存（同步到 Redis） | 201 / 409 |
| GET | `/api/inventory/{productId}` | 查询库存（带 Redis 缓存） | 200 / 404 |
| GET | `/api/inventory/{productId}/realtime` | Redis 实时库存（不经过 DB） | 200 |

### POST `/api/inventory/seed` — 初始化库存

**请求体**:
```json
{
  "productId": "550e8400-e29b-41d4-a716-446655440000",
  "quantity": 100
}
```

**响应 201**: 库存已创建并同步到 Redis
**响应 409**: 该商品库存已存在

### GET `/api/inventory/{productId}` — 查询库存

**响应 200**:
```json
{
  "id": "a1b2c3d4-...",
  "productId": "550e8400-e29b-41d4-a716-446655440000",
  "availableQuantity": 95,
  "reservedQuantity": 5
}
```

### GET `/api/inventory/{productId}/realtime` — 实时库存

直接从 Redis 读取 `stock:available:{id}` 和 `stock:reserved:{id}`，不经过数据库，适合高频率监控场景。

## 事件驱动

### 消费事件

| 事件 | 来源 | 消费者 | 行为 |
|------|------|--------|------|
| OrderPaidEvent | Payment 服务 | OrderPaidConsumer | 查询库存 → Deduct → 发布 StockDeductedEvent |

### 发布事件

| 事件 | 目标 | 触发时机 |
|------|------|----------|
| StockDeductedEvent | Shop 服务 | 库存扣减成功 |
| StockInsufficientEvent | Shop 服务 | 库存不足，扣减失败（当前无消费者处理） |

### 消息可靠性

- MassTransit **EntityFramework Outbox** 模式（MySQL）
- `AddInboxStateEntity()` 入站消息去重
- 重试策略：3 次间隔 5 秒
- 队列：Durable=true, AutoDelete=false, PurgeOnStartup=false

## 并发保护

| 机制 | 位置 |
|------|------|
| MassTransit Inbox 消息去重 | OrderPaidConsumer |
| ProductId 唯一索引 | 数据库层 |

## 依赖关系

```
WMS.API
  ├── WMS.Infrastructure
  │     ├── WMS.Application
  │     │     └── WMS.Domain
  │     └── WMS.Domain
  └── shared/Shop.Events
```

## 环境配置

| 配置项 | Development | Production |
|--------|:-----------:|:----------:|
| 日志级别 | Debug | Warning |
| 数据库连接 | `localhost:3307` | Docker 内部 (环境变量覆写) |
| Redis 库存 TTL | 120s | 30s |

## 本地运行

```bash
dotnet run --project services/WMS/src/WMS.API/WMS.API.csproj
```

Docker Compose 方式（推荐）：

```bash
docker-compose up -d wms-api
```

## 数据库

- 数据库：wms_db（MySQL 8.0，端口 3307）
- 表：inventories, MassTransitInboxState, MassTransitOutboxMessage, MassTransitOutboxState
- ORM：Entity Framework Core（Code-First）
- 启动时自动创建表（`EnsureCreated`）