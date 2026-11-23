using System.Diagnostics;
using DbReader;
using Derail;
using Npgsql;

namespace Orders;

internal sealed record ShipmentRequest(string? Carrier, string? Tracking);
internal sealed record ShipmentImportRow(long OrderId, string? Carrier, string? Tracking);
internal sealed record Shipment(string Carrier, string Tracking);
internal sealed record OrderShipment(long OrderId, Shipment Shipment);
internal sealed record ShipmentStatus(string Carrier, string Tracking, bool Delivered);
internal sealed record ShippingRow(string? Carrier, string? Tracking, bool Delivered);
internal sealed record OrderShipping(Option<Shipment> Shipment, bool Delivered);
internal enum ShipmentError { OrderNotFound, ShipmentNotFound, AlreadyDelivered }

internal static class ShipmentEndpoints
{
    public static Task<IResult> Put(long id, ShipmentRequest request, NpgsqlDataSource db, CancellationToken token) =>
        Validate(request.Carrier, request.Tracking).OnValid(shipment => ShipOrder(id, shipment)
            .InTransaction(db, token)
            .Match(saved => Results.Ok(saved), Problem));

    public static Task<IResult> Import(IReadOnlyList<ShipmentImportRow?> rows, NpgsqlDataSource db, CancellationToken token) =>
        Validation.Traverse(rows.Index(), row => Validate(row.Item?.Carrier, row.Item?.Tracking)
                .Map(shipment => new OrderShipment(row.Item?.OrderId ?? 0, shipment))
                .MapErrors(error => error with { Field = $"[{row.Index}].{error.Field}" }))
            .OnValid(shipments => ShipAll(shipments)
                .InTransaction(db, token)
                .Match(_ => Results.NoContent(), failure => Problem(failure.Error, failure.Index)));

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

    private static Validation<Shipment, FieldError> Validate(string? carrier, string? tracking) =>
        ApiInput.Field(carrier, "carrier", 40)
            .Zip(ApiInput.Field(tracking, "tracking", 80), (validCarrier, validTracking) => new Shipment(validCarrier, validTracking));

    private static AsyncReaderResult<NpgsqlConnection, Shipment, ShipmentError> ShipOrder(long id, Shipment shipment) =>
        Lock(id)
            .Ensure(order => !order.Delivered, _ => ShipmentError.AlreadyDelivered)
            .Map(order => Shipping.Ship(order.Shipment, shipment))
            .Bind(shipped => Save(id, shipped));

    // Loops inside one step rather than chaining a Bind per row, so a large batch does not deepen the call stack.
    private static AsyncReaderResult<NpgsqlConnection, int, (int Index, ShipmentError Error)> ShipAll(IReadOnlyList<OrderShipment> shipments) =>
        new(async (connection, token) =>
        {
            foreach (var (index, item) in shipments.Index())
            {
                if (await ShipOrder(item.OrderId, item.Shipment).Run(connection, token) is Error<ShipmentError>(var error))
                    return Result.Error<int, (int, ShipmentError)>((index, error));
            }
            return Result.Ok<int, (int, ShipmentError)>(shipments.Count);
        });

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

    private static AsyncReaderResult<NpgsqlConnection, Shipment, ShipmentError> Save(long id, Writer<Shipment, string> shipped) =>
        new(async (connection, token) =>
        {
            var (shipment, events) = shipped;
            await connection.ExecuteAsync(token, "UPDATE orders SET carrier = @Carrier, tracking = @Tracking WHERE id = @Id",
                new { Id = id, shipment.Carrier, shipment.Tracking });
            foreach (var kind in events)
                await connection.ExecuteAsync(token, "INSERT INTO order_events (order_id, kind) VALUES (@Id, @Kind)", new { Id = id, Kind = kind });
            return Result.Ok<Shipment, ShipmentError>(shipment);
        });

    private static AsyncReaderResult<NpgsqlConnection, int, ShipmentError> Execute(string sql, long id) =>
        new(async (connection, token) => Result.Ok<int, ShipmentError>(await connection.ExecuteAsync(token, sql, new { id })));

    private static IResult Problem(ShipmentError error) => Problem(error, null);

    private static IResult Problem(ShipmentError error, int? index) => error switch
    {
        ShipmentError.OrderNotFound => ApiResults.Problem(StatusCodes.Status404NotFound, "order-not-found", index),
        ShipmentError.ShipmentNotFound => ApiResults.Problem(StatusCodes.Status404NotFound, "shipment-not-found", index),
        ShipmentError.AlreadyDelivered => ApiResults.Problem(StatusCodes.Status409Conflict, "shipment-delivered", index),
        _ => throw new UnreachableException(),
    };
}
