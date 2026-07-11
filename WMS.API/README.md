# WMS.API — 库存微服务（API 层）

WMS 库存微服务的 Web API 表现层，提供库存 HTTP 接口、依赖注入注册、中间件配置和应用启动入口。

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

## API 接口

### POST /api/inventory/seed — 初始化库存

为指定商品创建库存记录。已存在的商品返回 409 冲突。

**请求体**
```json
{
  "productId": "550e8400-e29b-41d4-a716-446655440000",
  "quantity": 100
}
```

**示例**
```bash
curl -X POST http://localhost:53345/api/inventory/seed \
  -H "Content-Type: application/json" \
  -d '{"productId":"550e8400-e29b-41d4-a716-446655440000","quantity":100}'
```

### GET /api/inventory/{productId} — 查询库存

查询指定商品的实时库存，支持 Redis 缓存（TTL: 30s）。

**示例**
```bash
curl http://localhost:53345/api/inventory/550e8400-e29b-41d4-a716-446655440000
```

## 消息队列配置

- 消费队列：`wms-order-paid-queue`
- 消息重试：3 次，间隔 5 秒
- Outbox 模式：消息先写入 MySQL，再异步投递到 RabbitMQ

## 日志

- 控制台输出：`[{Timestamp:HH:mm:ss} {Level:u3}] {Service} | {Message}`
- 文件输出：`logs/wms-api-{yyyyMMdd}.log`（按天滚动）

## 启动

```bash
dotnet run --project WMS.API/WMS.API.csproj
```

Swagger: `http://localhost:53345/swagger`