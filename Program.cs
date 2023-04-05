using Dapper;
using Npgsql;
using MassTransit;
using DotnetDualWrite;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using Newtonsoft.Json;

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
    var orderCreated = new OrderCreated() { Amount = (decimal)11.22 };
    var orderJson = JsonConvert.SerializeObject(orderCreated);
    
    await using var conn = new NpgsqlConnection("Host=localhost:54321;Username=postgres;Password=postgres;Database=orders");
    await conn.ExecuteAsync(
        @"BEGIN TRANSACTION;
        SELECT * FROM pg_logical_emit_message(@Transactional, @Prefix, @Content);
        INSERT INTO orders_v2 (amount) VALUES (@Amount);
        COMMIT TRANSACTION;",
        new {
            Transactional = true,
            Prefix = "orders_outbox",
            Content = orderJson,
            Amount = (decimal)22.33
        }
    );

    return "new v2 order created!";
});

app.Run();

public class ReplicationConsumer : BackgroundService
{
    private IBus _bus;

    public ReplicationConsumer(IBus bus)
    {
        _bus = bus;
    }

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
        var options = new PgOutputReplicationOptions("test_pub", 1, messages: true);

        while (!stoppingToken.IsCancellationRequested)
        {
            await foreach (var message in replicationConn.StartReplication(slot, options, cancellationTokenSource.Token))
            {
                if (message is Npgsql.Replication.PgOutput.Messages.LogicalDecodingMessage)
                {
                    var decoding = message as Npgsql.Replication.PgOutput.Messages.LogicalDecodingMessage;
                    var reader = new StreamReader(decoding.Data);
                    var json = await reader.ReadToEndAsync();
                    Console.WriteLine($"Received message: {json}");
                    await _bus.Publish(JsonConvert.DeserializeObject<OrderCreated>(json));
                }

                replicationConn.SetReplicationStatus(message.WalEnd);
            }
        }
    }
}
