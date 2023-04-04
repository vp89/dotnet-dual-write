using Dapper;
using Npgsql;
using MassTransit;
using DotnetDualWrite;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ConfigureServices(cfg => {
    cfg.AddMassTransit(x =>
    {
        x.UsingRabbitMq((context,cfg) =>
        {
            cfg.Host("localhost", "/", h => {
                h.Username("guest");
                h.Password("guest");
            });
            cfg.ConfigureEndpoints(context);
        });
    });
});

var app = builder.Build();

// dual-write
app.MapPost("/v1/orders", async (IBus bus) => {
    await using var conn = new NpgsqlConnection("Host=localhost:54321;Username=postgres;Password=postgres;Database=orders");
    await conn.ExecuteAsync(
        "INSERT INTO orders (amount) VALUES (12.33);"
    );

    await bus.Publish(new OrderCreated() { Amount = (decimal)11.22 });

    return "new v1 order created!";
});

// improved pattern
app.MapPost("/v2/orders", async () => {
    await using var conn = new NpgsqlConnection("Host=localhost:54321;Username=postgres;Password=postgres;Database=orders");
    await conn.ExecuteAsync(
        "INSERT INTO orders_v2 (amount) VALUES (33.44);"
    );

    return "new v2 order created!";
});

app.Run();
