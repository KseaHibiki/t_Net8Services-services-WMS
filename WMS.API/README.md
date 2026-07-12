# WMS.API — 库存微服务（API 层）

WMS 库存微服务的 Web API 表现层，提供库存 HTTP 接口、DI 注册、中间件配置和应用启动入口。

## 核心职责

| 组件 | 文件 | 说明 |
|------|------|------|
| InventoryController | `Controllers/InventoryController.cs` | REST 接口（初始化/查询/实时） |
| Program.cs | `Program.cs` | DI 注册、MassTransit 配置、中间件、启动入口 |

## 技术栈

| 技术 | 用途 |
|------|------|
| ASP.NET Core 8 | Web 框架 |
| StackExchange.Redis 2.8.16 | Redis 客户端（缓存 + 库存同步） |
| MassTransit 8.3.0 | 消息总线（Outbox 发件箱） |
| Serilog 8.0 | 结构化日志 |
| Swashbuckle 6.5 | API 文档 |

## API 接口

| 方法 | 路径 | 说明 | 响应 |
|------|------|------|------|
| POST | `/api/inventory/seed` | 初始化库存（同步到 Redis） | 201 / 409 |
| GET | `/api/inventory/{productId}` | 查询库存（Redis 缓存 30s TTL） | 200 / 404 |
| GET | `/api/inventory/{productId}/realtime` | Redis 实时库存（不经过 DB） | 200 |

## 消息队列

- **消费队列**: `wms-order-paid-queue`
- **发布事件**: StockDeductedEvent, StockInsufficientEvent（通过 Outbox）
- **重试策略**: 3 次间隔 5 秒

## 日志

- 控制台: `[{HH:mm:ss} {Level:u3}] WMS.API | {Message}`
- 文件: `logs/wms-api-{yyyyMMdd}.log`（按天滚动）

## 环境配置

| 配置项 | Development | Production |
|--------|:-----------:|:----------:|
| 日志级别 | Debug | Warning |
| 数据库 | `localhost:3307` | Docker 内部 |
| Redis 库存 TTL | 120s | 30s |

## 启动

```bash
dotnet run --project WMS.API/WMS.API.csproj
```

Swagger: `http://localhost:5002/swagger`