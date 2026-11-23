using Derail;
using Npgsql;

namespace Orders;

internal static class OrderStore
{
    public static AsyncReaderResult<NpgsqlDataSource, Shipment, ShipError> Ship(long id, Shipment shipment) =>
        new(async (db, token) =>
        {
            await using var connection = await db.OpenConnectionAsync(token);
            await using var tx = await connection.BeginTransactionAsync(token);
            var snapshot = await FindForUpdate(connection, tx, id, token);
            var proposal = Shipping.Decide(shipment).Run(snapshot);
            return await proposal.BindAsync(async writer =>
            {
                foreach (var shipped in writer.Events)
                    await RecordShipment(connection, tx, id, shipped, token);
                await tx.CommitAsync(token);
                return Result.Ok<Shipment, ShipError>(writer.Value);
            });
        });

    private static async Task<Option<Order>> FindForUpdate(NpgsqlConnection connection, NpgsqlTransaction tx, long id, CancellationToken token)
    {
        await using var cmd = new NpgsqlCommand("SELECT carrier, tracking FROM orders WHERE id = $1 FOR UPDATE", connection, tx);
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return Option.None;
        return Option.Some(new Order(reader.IsDBNull(0) ? null : new Shipment(reader.GetString(0), reader.GetString(1))));
    }

    private static async Task RecordShipment(NpgsqlConnection connection, NpgsqlTransaction tx, long id, OrderShipped shipped, CancellationToken token)
    {
        await using var update = new NpgsqlCommand("UPDATE orders SET carrier = $1, tracking = $2 WHERE id = $3", connection, tx);
        update.Parameters.AddWithValue(shipped.Shipment.Carrier);
        update.Parameters.AddWithValue(shipped.Shipment.Tracking);
        update.Parameters.AddWithValue(id);
        await update.ExecuteNonQueryAsync(token);

        await using var append = new NpgsqlCommand("INSERT INTO order_events (order_id, kind) VALUES ($1, 'order-shipped')", connection, tx);
        append.Parameters.AddWithValue(id);
        await append.ExecuteNonQueryAsync(token);
    }
}
