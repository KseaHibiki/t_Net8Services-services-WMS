# WMS.API — 库存微服务

Warehouse Management System，负责电商系统的库存管理。对接收到的新订单事件进行库存扣减，支持库存初始化和实时查询。

## 架构

| 层 | 项目 | 职责 |
|---|---|---|
| Domain | WMS.Domain | 聚合根 `Inventory`、`IInventoryRepository` 接口 |
| Application | WMS.Application | 事件消费者（`OrderCreatedConsumer`） |
| Infrastructure | WMS.Infrastructure | EF Core DbContext、`InventoryRepository` 实现 |
| API | WMS.API | Controllers、依赖注入注册、Swagger、启动入口 |

## 技术栈

- **框架**: ASP.NET Core 8
- **ORM**: Entity Framework Core + MySQL (Pomelo)
- **消息队列**: MassTransit + RabbitMQ
- **数据库**: MySQL 8.0（数据库 wms_db，端口 3307）

## API 端点

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/api/inventory/seed` | 初始化（或追加）库存数据 |
| GET | `/api/inventory/{productId}` | 查询指定商品库存 |

## 事件参与

| 事件 | 方向 | 说明 |
|---|---|---|
| `OrderCreatedEvent` | 消费 | 接单后扣减库存，如果库存不足则发布 `StockInsufficientEvent` |
| `StockDeductedEvent` | 发布 | 成功扣减后通知 Shop 确认订单 |

## 本地运行

```bash
# 依赖 MySQL (3307) 和 RabbitMQ (5672)
dotnet run --project services/WMS/src/WMS.API/WMS.API.csproj
```

API 地址: `http://localhost:5002/swagger`
