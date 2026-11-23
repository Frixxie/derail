using DbReader;
using Derail;
using Npgsql;

namespace Orders;

internal static class OrderStore
{
    private sealed record OrderRow(long Id, string? Carrier, string? Tracking)
    {
        public Order ToOrder() => new(Id, (Carrier, Tracking) switch
        {
            (null, null) => null,
            ({ } carrier, { } tracking) => new Shipment(carrier, tracking),
            _ => throw new InvalidOperationException("Incomplete shipment in order row."),
        });
    }

    public static AsyncReaderResult<NpgsqlDataSource, IReadOnlyList<Order>, OrderNotFound> Lookup(NonEmpty<long> ids) =>
        new(async (db, token) =>
        {
            await using var connection = await db.OpenConnectionAsync(token);
            var rows = await connection.ReadAsync<OrderRow>(token,
                "SELECT id, carrier, tracking FROM orders WHERE id IN (@Ids)", new { Ids = ids.Distinct().ToArray() });
            var orders = rows.ToDictionary(row => row.Id, row => row.ToOrder());
            return Result.Traverse(ids, id => Option.FromNullable(orders.GetValueOrDefault(id)).ToResult(() => new OrderNotFound(id)));
        });

    public static AsyncReaderResult<NpgsqlDataSource, Shipment, ShipError> Ship(long id, Shipment shipment) =>
        new(async (db, token) =>
        {
            await using var connection = await db.OpenConnectionAsync(token);
            await using var tx = await connection.BeginTransactionAsync(token);
            var snapshot = await FindForUpdate(connection, tx, id, token);
            var proposal = Shipping.Decide(id, shipment).Run(snapshot);
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
        var rows = await connection.ReadAsync<OrderRow>(token,
            "SELECT id, carrier, tracking FROM orders WHERE id = @Id FOR UPDATE", new { Id = id }, cmd => cmd.Transaction = tx);
        return Option.FromNullable(rows.SingleOrDefault()).Map(row => row.ToOrder());
    }

    private static async Task RecordShipment(NpgsqlConnection connection, NpgsqlTransaction tx, long id, OrderShipped shipped, CancellationToken token)
    {
        await connection.ExecuteAsync(token, "UPDATE orders SET carrier = @Carrier, tracking = @Tracking WHERE id = @Id",
            new { Id = id, shipped.Shipment.Carrier, shipped.Shipment.Tracking }, cmd => cmd.Transaction = tx);
        await connection.ExecuteAsync(token, "INSERT INTO order_events (order_id, kind) VALUES (@Id, 'order-shipped')",
            new { Id = id }, cmd => cmd.Transaction = tx);
    }
}
