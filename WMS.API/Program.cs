using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;
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
    x.AddConsumer<OrderCreatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq")
            ?? "amqp://guest:guest@localhost:5672/");

        cfg.ReceiveEndpoint("wms-order-created-queue", e =>
        {
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
