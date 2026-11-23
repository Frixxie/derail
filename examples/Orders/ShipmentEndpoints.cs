using System.Diagnostics;
using DbReader;
using Derail;
using Npgsql;

namespace Orders;

internal sealed record ShipmentRequest(string? Carrier, string? Tracking);
internal sealed record Shipment(string Carrier, string Tracking);
internal sealed record ShipmentStatus(string Carrier, string Tracking, bool Delivered);
internal sealed record ShippingRow(string? Carrier, string? Tracking, bool Delivered);
internal sealed record OrderShipping(Option<Shipment> Shipment, bool Delivered);
internal enum ShipmentError { OrderNotFound, ShipmentNotFound, AlreadyDelivered }

internal static class ShipmentEndpoints
{
    public static Task<IResult> Put(long id, ShipmentRequest request, NpgsqlDataSource db, CancellationToken token) =>
        ApiInput.Field(request.Carrier, "carrier", 40)
            .Zip(ApiInput.Field(request.Tracking, "tracking", 80), (carrier, tracking) => new Shipment(carrier, tracking))
            .OnValid(shipment => Lock(id)
                .Ensure(order => !order.Delivered, _ => ShipmentError.AlreadyDelivered)
                .Bind(_ => Save(id, shipment))
                .InTransaction(db, token)
                .Match(saved => Results.Ok(saved), Problem));

    public static async Task<IResult> Get(long id, NpgsqlDataSource db, CancellationToken token)
    {
        await using var connection = await db.OpenConnectionAsync(token);
        var rows = await connection.ReadAsync<ShipmentStatus>(token,
            "SELECT carrier, tracking, delivered FROM orders WHERE id = @Id AND carrier IS NOT NULL", new { id });
        return Option.FromNullable(rows.SingleOrDefault()).OkOrNotFound("shipment-not-found");
    }

    public static Task<IResult> Delete(long id, NpgsqlDataSource db, CancellationToken token) =>
        Lock(id)
            .Ensure(order => order.Shipment.IsSome, _ => ShipmentError.ShipmentNotFound)
            .Ensure(order => !order.Delivered, _ => ShipmentError.AlreadyDelivered)
            .Bind(_ => Execute("UPDATE orders SET carrier = NULL, tracking = NULL WHERE id = @Id", id))
            .InTransaction(db, token)
            .Match(_ => Results.NoContent(), Problem);

    public static Task<IResult> Deliver(long id, NpgsqlDataSource db, CancellationToken token) =>
        Lock(id)
            .Ensure(order => order.Shipment.IsSome, _ => ShipmentError.ShipmentNotFound)
            .Bind(_ => Execute("UPDATE orders SET delivered = true WHERE id = @Id", id))
            .InTransaction(db, token)
            .Match(_ => Results.NoContent(), Problem);

    private static AsyncReaderResult<NpgsqlConnection, OrderShipping, ShipmentError> Lock(long id) =>
        new(async (connection, token) =>
        {
            var rows = await connection.ReadAsync<ShippingRow>(token,
                "SELECT carrier, tracking, delivered FROM orders WHERE id = @Id FOR UPDATE", new { id });
            return Option.FromNullable(rows.SingleOrDefault())
                .Map(row => new OrderShipping(
                    Option.FromNullable(row.Carrier).Zip(Option.FromNullable(row.Tracking), (carrier, tracking) => new Shipment(carrier, tracking)),
                    row.Delivered))
                .ToResult(() => ShipmentError.OrderNotFound);
        });

    private static AsyncReaderResult<NpgsqlConnection, Shipment, ShipmentError> Save(long id, Shipment shipment) =>
        new(async (connection, token) =>
        {
            await connection.ExecuteAsync(token, "UPDATE orders SET carrier = @Carrier, tracking = @Tracking WHERE id = @Id",
                new { Id = id, shipment.Carrier, shipment.Tracking });
            await connection.ExecuteAsync(token,
                "INSERT INTO order_events (order_id, kind) VALUES (@Id, 'order-shipped') ON CONFLICT DO NOTHING", new { Id = id });
            return Result.Ok<Shipment, ShipmentError>(shipment);
        });

    private static AsyncReaderResult<NpgsqlConnection, int, ShipmentError> Execute(string sql, long id) =>
        new(async (connection, token) => Result.Ok<int, ShipmentError>(await connection.ExecuteAsync(token, sql, new { id })));

    private static IResult Problem(ShipmentError error) => error switch
    {
        ShipmentError.OrderNotFound => ApiResults.NotFound("order-not-found"),
        ShipmentError.ShipmentNotFound => ApiResults.NotFound("shipment-not-found"),
        ShipmentError.AlreadyDelivered => ApiResults.Problem(StatusCodes.Status409Conflict, "shipment-delivered"),
        _ => throw new UnreachableException(),
    };
}
