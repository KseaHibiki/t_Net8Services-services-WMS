# WMS 库存微服务

**Warehouse Management System** — 基于 .NET 8 构建的库存管理微服务，采用领域驱动设计（DDD）四层架构，是电商平台中负责库存管理的核心服务。

## 架构概览

```
┌──────────────────────────────────────────────────────────┐
│  WMS.API          (Web API / 表现层)                      │
│  - InventoryController   REST 接口                        │
│  - Program.cs            启动入口（DI、中间件、Swagger）    │
├──────────────────────────────────────────────────────────┤
│  WMS.Application   (应用层)                               │
│  - OrderPaidConsumer     事件消费者（支付后扣库存）          │
├──────────────────────────────────────────────────────────┤
│  WMS.Domain        (领域层)                               │
│  - Inventory             库存聚合根                         │
│  - IInventoryRepository  仓储接口                          │
├──────────────────────────────────────────────────────────┤
│  WMS.Infrastructure (基础设施层)                            │
│  - WmsDbContext          EF Core 数据库上下文               │
│  - InventoryRepository   仓储实现                          │
└──────────────────────────────────────────────────────────┘
```

| 层 | 项目 | 职责 |
|---|---|---|
| Domain | `WMS.Domain` | 聚合根、业务规则、仓储接口 |
| Application | `WMS.Application` | 事件消费者、编排用例 |
| Infrastructure | `WMS.Infrastructure` | EF Core 持久化、仓储实现 |
| API | `WMS.API` | 控制器、依赖注入、Swagger 文档、启动配置 |

## 技术栈

| 技术 | 用途 | 版本 |
|---|---|---|
| ASP.NET Core | Web 框架 | 8.0 |
| Entity Framework Core | ORM | 8.0 |
| MySQL (Pomelo) | 数据库 | 8.0 |
| MassTransit | 消息总线 | 8.3.0 |
| RabbitMQ | 消息队列 | — |
| Redis (StackExchange) | 缓存 | 2.8.16 |
| Serilog | 结构化日志 | 8.0 |
| Swashbuckle | API 文档 | 6.5 |

## 领域模型

### Inventory 库存聚合根

库存采用**两阶段锁定模式**：下单时 `Deduct` 预占库存（可用→预占），订单完成时 `ConfirmDeduction` 释放预占。

```
┌──────────────────────────────┐
│         Inventory            │
├──────────────────────────────┤
│  Id               : Guid     │
│  ProductId        : Guid     │
│  AvailableQuantity : int     │  ← 可用库存
│  ReservedQuantity  : int     │  ← 已预占库存
├──────────────────────────────┤
│  + Create(productId, qty)    │  创建库存
│  + Deduct(qty)               │  预占库存（可用 → 预占）
│  + ConfirmDeduction(qty)     │  确认扣减（释放预占）
│  + Replenish(qty)            │  补货（增加可用）
└──────────────────────────────┘
```

**业务规则：**
- 可用库存不能为负
- `Deduct` 时可用不足抛 `InvalidOperationException`
- 数量参数 <= 0 抛 `ArgumentException`

## 事件驱动流程

### 消费的事件

| 事件 | 来源 | 说明 |
|---|---|---|
| `OrderPaidEvent` | 订单服务 | 收到支付成功通知后扣减库存 |

### 发布的事件

| 事件 | 去向 | 说明 |
|---|---|---|
| `StockDeductedEvent` | 订单服务 | 库存扣减成功 |
| `StockInsufficientEvent` | 订单服务 | 库存不足，扣减失败 |

### 核心流程

```
订单服务                          WMS
    │                              │
    │  ─ OrderPaidEvent ─────────► │
    │                              │
    │                              ├─ 查询库存
    │                              │
    │                              ├─ 库存不存在？
    │                              │   └─ 发布 StockInsufficientEvent
    │                              │
    │                              ├─ 库存足够？
    │                              │   ├─ Deduct(quantity)
    │                              │   ├─ SaveChanges
    │                              │   ├─ 发布 StockDeductedEvent
    │                              │   └─ 可用 < 100？
    │                              │       └─ Replenish(500)
    │                              │
    │                              └─ 库存不足？
    │                                  └─ 发布 StockInsufficientEvent
    │                              │
    │  ◄── StockDeductedEvent ─── │
    │  ◄── StockInsufficientEvent │
```

### 消息持久化（Transactional Outbox）

消息采用 **Transactional Outbox 模式**，确保消息不丢失：

```
Consumer 执行
    │
    ├─ 1. 业务变更（EF Core 追踪）
    ├─ 2. context.Publish(event)  → 消息写入 OutboxMessage 表
    ├─ 3. SaveChangesAsync
    │      └─ 业务变更 + OutboxMessage 在同一数据库事务提交
    │
    └─ 4. Bus Outbox 后台服务轮询 OutboxMessage 表
           └─ 投递到 RabbitMQ → 删除已投递记录
```

**数据库中自动创建的表（通过 `EnsureCreated`）：**

| 表名 | 用途 |
|---|---|
| `InboxState` | 消息去重（入站幂等性） |
| `OutboxMessage` | 待投递消息存储 |
| `OutboxState` | 投递批次跟踪 |

**配置特性：**
- 重试策略：`UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)))` — 失败 3 次，每次间隔 5 秒
- 队列配置：`Durable = true`，消息持久化到磁盘

## API 接口

### GET /api/Inventory/{productId} — 查询库存

查询指定商品的实时库存。

**路径参数：**

| 参数 | 类型 | 说明 |
|---|---|---|
| productId | guid | 商品 ID |

**缓存逻辑：**
1. 先查 Redis（Key: `inventory:{productId}`, TTL: 30s）
2. 缓存命中 → 直接返回
3. 缓存未命中 → 查数据库 → 写入缓存后返回

**响应 200：**
```json
{
  "id": "a1b2c3d4-...",
  "productId": "550e8400-e29b-41d4-a716-446655440000",
  "availableQuantity": 95,
  "reservedQuantity": 5
}
```

**响应 404：**
```json
{
  "message": "Inventory for product 550e8400-... not found."
}
```

### POST /api/Inventory/seed — 初始化库存

为商品播种初始库存。

**请求体：**
```json
{
  "productId": "550e8400-e29b-41d4-a716-446655440000",
  "quantity": 100
}
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| productId | guid | 是 | 商品 ID |
| quantity | int | 是 | 初始库存数量 |

**响应 201：** 库存已创建
**响应 409：** 该商品库存已存在

## 自动补货（测试功能）

订单支付扣减库存后，若商品的 `AvailableQuantity < 100`，系统自动补货 500 件。

```csharp
if (inventory.AvailableQuantity < 100)
{
    inventory.Replenish(500);
    await _inventoryRepository.UpdateAsync(inventory);
    await _inventoryRepository.SaveChangesAsync();
}
```

此功能用于测试低库存场景下自动补货的完整链路。

## 本地运行

### 前置依赖

- Docker（MySQL + RabbitMQ + Redis）
- .NET 8 SDK

### 启动依赖服务

```bash
# 启动 MySQL (3307)、RabbitMQ (5672)、Redis (6379)
docker run -d --name wms-mysql -p 3307:3306 -e MYSQL_ROOT_PASSWORD=114514 -e MYSQL_DATABASE=wms_db mysql:8
docker run -d --name wms-rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:4-management
docker run -d --name wms-redis -p 6379:6379 redis:7
```

### 启动服务

```bash
cd services/WMS/src
dotnet run --project WMS.API/WMS.API.csproj

# 默认启动环境变量
ASPNETCORE_ENVIRONMENT=Development
```

API 地址：`http://localhost:53345/swagger`

### 连接字符串配置

`appsettings.json` 中可配置：

```json
{
  "ConnectionStrings": {
    "wms": "Server=wms-mysql;Port=3306;Database=wms_db;User=root;Password=114514;",
    "rabbitmq": "amqp://guest:guest@rabbitmq:5672/",
    "redis": "localhost:6379"
  },
  "RedisCache": {
    "InventoryTtlSeconds": 30
  }
}
```

> 本地开发时可修改 `wms` 连接串为 `Server=localhost;Port=3307`，RabbitMQ 为 `localhost`。

## 项目结构

```
services/WMS/src/
├── WMS.API/                        # API 表现层
│   ├── Controllers/
│   │   └── InventoryController.cs  库存 REST 接口
│   ├── Program.cs                  启动入口
│   ├── Dockerfile                  容器镜像
│   ├── appsettings.json            应用配置
│   └── WMS.API.csproj
│
├── WMS.Application/                # 应用层
│   ├── Consumers/
│   │   └── OrderPaidConsumer.cs    订单支付事件消费者
│   └── WMS.Application.csproj
│
├── WMS.Domain/                     # 领域层
│   ├── Aggregates/
│   │   ├── Inventory.cs            库存聚合根
│   │   └── IInventoryRepository.cs 仓储接口
│   └── WMS.Domain.csproj
│
├── WMS.Infrastructure/             # 基础设施层
│   └── Persistence/
│       ├── WmsDbContext.cs         EF Core 数据库上下文
│       └── InventoryRepository.cs  仓储实现
│
└── shared/
    └── Shop.Events/                # 共享事件定义
        ├── OrderPaidEvent.cs       订单已支付
        ├── StockDeductedEvent.cs   库存已扣减
        ├── StockInsufficientEvent.cs 库存不足
        ├── OrderCreatedEvent.cs    订单已创建
        └── OrderCompletedEvent.cs  订单已完成

```

## 数据库表结构

### inventories

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| Id | char(36) | PK | 主键 |
| ProductId | char(36) | UNIQUE, NOT NULL | 商品 ID（唯一索引） |
| AvailableQuantity | int | NOT NULL | 可用库存 |
| ReservedQuantity | int | NOT NULL | 已预占库存 |

### MassTransit Outbox 表

| 表名 | 说明 |
|---|---|
| MassTransitInboxState | 入站消息去重 |
| MassTransitOutboxMessage | 出站消息持久化 |
| MassTransitOutboxState | 出站批次状态 |
