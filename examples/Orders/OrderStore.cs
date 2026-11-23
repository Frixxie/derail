using DbReader;
using Derail;
using Npgsql;

namespace Orders;

internal static class OrderStore
{
    private sealed record OrderRow(long Id, string? Carrier, string? Tracking);

    public static async Task<Result<Shipment, ShipError>> Ship(
        NpgsqlDataSource db, long id, Shipment shipment, CancellationToken token)
    {
        await using var connection = await db.OpenConnectionAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        var rows = await connection.ReadAsync<OrderRow>(token,
            "SELECT id, carrier, tracking FROM orders WHERE id = @Id FOR UPDATE", new { Id = id }, cmd => cmd.Transaction = tx);

        var order = rows.SingleOrDefault();
        if (order is null) return new Error<ShipError>(new OrderNotFound());
        if (order.Carrier == shipment.Carrier && order.Tracking == shipment.Tracking) return new Ok<Shipment>(shipment);
        if (order.Carrier is not null) return new Error<ShipError>(new AlreadyShipped());

        await connection.ExecuteAsync(token, "UPDATE orders SET carrier = @Carrier, tracking = @Tracking WHERE id = @Id",
            new { Id = id, shipment.Carrier, shipment.Tracking }, cmd => cmd.Transaction = tx);
        await connection.ExecuteAsync(token, "INSERT INTO order_events (order_id, kind) VALUES (@Id, 'order-shipped')",
            new { Id = id }, cmd => cmd.Transaction = tx);
        await tx.CommitAsync(token);
        return new Ok<Shipment>(shipment);
    }
}
