using Dapper;
using Npgsql;
using MassTransit;
using DotnetDualWrite;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;

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

    cfg.AddHostedService<ReplicationConsumer>();
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

public class ReplicationConsumer : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var conn = new NpgsqlConnection("Host=localhost:54321;Username=postgres;Password=postgres;Database=orders"))
        {
            await conn.ExecuteAsync("SELECT * FROM pg_create_logical_replication_slot('test_slot', 'pgoutput')");
        }

        await using var replicationConn = new LogicalReplicationConnection("Host=localhost:54321;Username=postgres;Password=postgres;Database=orders");
        await replicationConn.Open();

        var slot = new PgOutputReplicationSlot("test_slot");
        var cancellationTokenSource = new CancellationTokenSource();
        var options = new PgOutputReplicationOptions("test_slot", 1);

        while (!stoppingToken.IsCancellationRequested)
        {
            await foreach (var message in replicationConn.StartReplication(slot, options, cancellationTokenSource.Token))
            {
                Console.WriteLine($"Received message type: {message.GetType().Name} {message.WalStart} {message.WalEnd} {message.ToString()}");
                replicationConn.SetReplicationStatus(message.WalEnd);
            }
        }
    }
}
