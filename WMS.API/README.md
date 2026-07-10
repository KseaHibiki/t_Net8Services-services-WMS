# WMS.API — 库存微服务

Warehouse Management System，负责电商系统的库存管理。对接收到的新订单事件进行库存扣减，支持库存初始化和实时查询。

## 架构

| 层 | 项目 | 职责 |
|---|---|---|
| Domain | WMS.Domain | 聚合根 `Inventory`、`IInventoryRepository` 接口 |
| Application | WMS.Application | 事件消费者（`OrderCreatedConsumer`） |
| Infrastructure | WMS.Infrastructure | EF Core DbContext、`InventoryRepository` 实现 |
| API | WMS.API | Controllers、依赖注入注册、Swagger、Serilog 日志、启动入口 |

## 技术栈

- **框架**: ASP.NET Core 8
- **ORM**: Entity Framework Core + MySQL (Pomelo)
- **消息队列**: MassTransit + RabbitMQ
- **日志**: Serilog（控制台 + 按天滚动文件）
- **数据库**: MySQL 8.0（数据库 wms_db，端口 3307）

## API 接口

### POST /api/inventory/seed — 初始化库存

为指定商品创建库存记录。如果商品已存在库存则返回 409 冲突。

**请求体**

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

**响应** — `201 Created`

```json
{
  "id": "a1b2c3d4-...",
  "productId": "550e8400-e29b-41d4-a716-446655440000",
  "availableQuantity": 100,
  "reservedQuantity": 0
}
```

**错误响应** — `409 Conflict`

```json
{
  "message": "Inventory for product 550e8400-... already exists."
}
```

**示例**

```bash
curl -X POST http://localhost:5002/api/inventory/seed \
  -H "Content-Type: application/json" \
  -d '{"productId":"550e8400-e29b-41d4-a716-446655440000","quantity":100}'
```

---

### GET /api/inventory/{productId} — 查询库存

查询指定商品的当前库存信息。

**路径参数**

| 参数 | 类型 | 说明 |
|---|---|---|
| productId | guid | 商品 ID |

**响应** — `200 OK`

```json
{
  "id": "a1b2c3d4-...",
  "productId": "550e8400-e29b-41d4-a716-446655440000",
  "availableQuantity": 95,
  "reservedQuantity": 5
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| id | guid | 库存记录 ID |
| productId | guid | 商品 ID |
| availableQuantity | int | 可用库存（扣除已预留） |
| reservedQuantity | int | 已预留库存 |

**错误响应** — `404 Not Found`

**示例**

```bash
curl http://localhost:5002/api/inventory/550e8400-e29b-41d4-a716-446655440000
```

## 事件参与

| 事件 | 方向 | 说明 |
|---|---|---|
| `OrderCreatedEvent` | 消费 | 收到后扣减库存，不足则发布 `StockInsufficientEvent` |
| `StockDeductedEvent` | 发布 | 扣减成功后通知 Shop 确认订单 |
| `StockInsufficientEvent` | 发布 | 库存不足时通知 Shop |

## 自动补货逻辑

库存扣减后，如果可用库存低于 100 件，系统自动补充 500 件。此逻辑在 `OrderCreatedConsumer` 中实现。

## 本地运行

```bash
# 依赖 MySQL (3307) 和 RabbitMQ (5672)
dotnet run --project services/WMS/src/WMS.API/WMS.API.csproj
```

API 地址: `http://localhost:5002/swagger`