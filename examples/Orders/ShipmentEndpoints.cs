using DbReader;
using Npgsql;

namespace Orders;

internal sealed record ShipmentRequest(string? Carrier, string? Tracking);
internal sealed record Shipment(string Carrier, string Tracking);

internal static class ShipmentEndpoints
{
    public static Task<IResult> Put(long id, ShipmentRequest request, NpgsqlDataSource db, CancellationToken token) =>
        ApiInput.Field(request.Carrier, "carrier", 40)
            .Zip(ApiInput.Field(request.Tracking, "tracking", 80), (carrier, tracking) => new Shipment(carrier, tracking))
            .OnValid(async shipment =>
            {
                var result = await Save(db, id, shipment, token);
                return result.Match(value => Results.Ok(value), () => ApiInput.NotFound("order-not-found"));
            });

    private static async Task<Option<Shipment>> Save(
        NpgsqlDataSource db, long id, Shipment shipment, CancellationToken token)
    {
        await using var connection = await db.OpenConnectionAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        var count = await connection.ExecuteAsync(token, "UPDATE orders SET carrier = @Carrier, tracking = @Tracking WHERE id = @Id",
            new { Id = id, shipment.Carrier, shipment.Tracking }, cmd => cmd.Transaction = tx);
        if (count == 0) return Option.None;
        await connection.ExecuteAsync(token, "INSERT INTO order_events (order_id, kind) VALUES (@Id, 'order-shipped') ON CONFLICT DO NOTHING",
            new { Id = id }, cmd => cmd.Transaction = tx);
        await tx.CommitAsync(token);
        return Option.Some(shipment);
    }

    public static async Task<IResult> Get(long id, NpgsqlDataSource db, CancellationToken token)
    {
        await using var connection = await db.OpenConnectionAsync(token);
        var rows = await connection.ReadAsync<Shipment>(token,
            "SELECT carrier, tracking FROM orders WHERE id = @Id AND carrier IS NOT NULL", new { id });
        return rows.SingleOrDefault() is { } shipment ? Results.Ok(shipment) : ApiInput.NotFound("shipment-not-found");
    }

    public static async Task<IResult> Delete(long id, NpgsqlDataSource db, CancellationToken token)
    {
        await using var connection = await db.OpenConnectionAsync(token);
        var count = await connection.ExecuteAsync(token,
            "UPDATE orders SET carrier = NULL, tracking = NULL WHERE id = @Id AND carrier IS NOT NULL", new { id });
        return count == 1 ? Results.NoContent() : ApiInput.NotFound("shipment-not-found");
    }
}
