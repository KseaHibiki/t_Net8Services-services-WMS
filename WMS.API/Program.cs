using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;
using WMS.Application.Consumers;
using WMS.Domain.Aggregates;
using WMS.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "WMS.API")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} | {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/wms-api-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate:
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Service} | {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Redis
var redisConnectionString = builder.Configuration.GetConnectionString("redis")
    ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));

// Database
var connectionString = builder.Configuration.GetConnectionString("wms")
    ?? "Server=localhost;Port=3307;Database=wms_db;User=root;Password=114514;";
builder.Services.AddDbContext<WmsDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// Repositories
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();

// MassTransit
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderPaidConsumer>();
    x.AddConsumer<OrderCreatedConsumer>();

    x.AddEntityFrameworkOutbox<WmsDbContext>(o =>
    {
        o.QueryDelay = TimeSpan.FromSeconds(1);
        o.UseMySql();
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq")
            ?? "amqp://guest:guest@localhost:5672/");

        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

        cfg.ReceiveEndpoint("wms-order-paid-queue", e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PurgeOnStartup = false;

            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

            e.ConfigureConsumer<OrderPaidConsumer>(context);
        });

        cfg.ReceiveEndpoint("wms-order-created-queue", e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PurgeOnStartup = false;

            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

            e.ConfigureConsumer<OrderCreatedConsumer>(context);
        });
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
    db.Database.EnsureCreated();
    Log.Information("数据库初始化完成 (wms_db)");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

app.MapControllers();

Log.Information("WMS.API 启动完成，监听端口 5002");
app.Run();
