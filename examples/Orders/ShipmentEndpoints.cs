using System.Diagnostics;
using DbReader;
using Derail;
using Npgsql;

namespace Orders;

internal sealed record ShipmentRequest(string? Carrier, string? Tracking);
internal sealed record ShipmentImportRow(long? OrderId, string? Carrier, string? Tracking);
internal sealed record Shipment(string Carrier, string Tracking);
internal sealed record OrderShipment(long OrderId, Shipment Shipment);
internal sealed record ShipmentStatus(string Carrier, string Tracking, bool Delivered);
internal sealed record ShippingRow(string? Carrier, string? Tracking, bool Delivered);
internal sealed record OrderShipping(Option<Shipment> Shipment, bool Delivered);
internal enum ShipmentError { OrderNotFound, ShipmentNotFound, AlreadyDelivered }

internal static class ShipmentEndpoints
{
    private const string ReadShipping = "SELECT carrier, tracking, delivered FROM orders WHERE id = @Id";
    private const string LockShipping = $"{ReadShipping} FOR UPDATE";

    public static Task<IResult> Put(long id, ShipmentRequest request, NpgsqlDataSource db, CancellationToken token) =>
        Validate(request.Carrier, request.Tracking).OnValid(shipment => ShipOrder(id, shipment)
            .InTransaction(db, token)
            .Match(saved => Results.Ok(saved), Problem));

    public static Task<IResult> Import(IReadOnlyList<ShipmentImportRow?> rows, NpgsqlDataSource db, CancellationToken token) =>
        NonEmpty.FromEnumerable(rows.Index())
            .Match(
                requested => Validation.Traverse(requested, row => ImportRow(row.Item)
                    .MapErrors(error => error with { Field = $"[{row.Index}].{error.Field}" })),
                () => Validation.Invalid<NonEmpty<OrderShipment>, FieldError>(new("shipments", "required")))
            .OnValid(shipments => ShipAll(shipments)
                .InTransaction(db, token)
                .Match(_ => Results.NoContent(), failure => Problem(failure.Error, failure.Index)));

    public static Task<IResult> Get(long id, NpgsqlDataSource db, CancellationToken token) =>
        Load(ReadShipping, id)
            .Bind(order => AsyncReaderResult.FromResult<NpgsqlConnection, ShipmentStatus, ShipmentError>(order.Shipment
                .Map(shipment => new ShipmentStatus(shipment.Carrier, shipment.Tracking, order.Delivered))
                .ToResult(() => ShipmentError.ShipmentNotFound)))
            .InTransaction(db, token)
            .Match(status => Results.Ok(status), Problem);

    public static Task<IResult> Delete(long id, NpgsqlDataSource db, CancellationToken token) =>
        Load(LockShipping, id)
            .Ensure(order => order.Shipment.IsSome, _ => ShipmentError.ShipmentNotFound)
            .Ensure(order => !order.Delivered, _ => ShipmentError.AlreadyDelivered)
            .Bind(_ => Change(id, "UPDATE orders SET carrier = NULL, tracking = NULL WHERE id = @Id", "shipment-removed"))
            .InTransaction(db, token)
            .Match(_ => Results.NoContent(), Problem);

    // Repeating a delivery succeeds without recording a second event.
    public static Task<IResult> Deliver(long id, NpgsqlDataSource db, CancellationToken token) =>
        Load(LockShipping, id)
            .Ensure(order => order.Shipment.IsSome, _ => ShipmentError.ShipmentNotFound)
            .Bind(order => order.Delivered
                ? AsyncReaderResult.Pure<NpgsqlConnection, long, ShipmentError>(id)
                : Change(id, "UPDATE orders SET delivered = true WHERE id = @Id", "shipment-delivered"))
            .InTransaction(db, token)
            .Match(_ => Results.NoContent(), Problem);

    private static Validation<Shipment, FieldError> Validate(string? carrier, string? tracking) =>
        ApiInput.Field(carrier, "carrier", 40)
            .Zip(ApiInput.Field(tracking, "tracking", 80), (validCarrier, validTracking) => new Shipment(validCarrier, validTracking));

    private static Validation<OrderShipment, FieldError> ImportRow(ShipmentImportRow? row) =>
        ApiInput.Positive(row?.OrderId, "orderId")
            .Zip(Validate(row?.Carrier, row?.Tracking), (orderId, shipment) => new OrderShipment(orderId, shipment));

    private static AsyncReaderResult<NpgsqlConnection, Shipment, ShipmentError> ShipOrder(long id, Shipment shipment) =>
        Load(LockShipping, id)
            .Ensure(order => !order.Delivered, _ => ShipmentError.AlreadyDelivered)
            .Map(order => Shipping.Ship(order.Shipment, shipment))
            .Bind(shipped => Save(id, shipped));

    // Loops inside one step rather than chaining a Bind per row, so a large batch does not deepen the call stack.
    private static AsyncReaderResult<NpgsqlConnection, int, (int Index, ShipmentError Error)> ShipAll(NonEmpty<OrderShipment> shipments) =>
        new(async (connection, token) =>
        {
            foreach (var (index, item) in shipments.Index())
            {
                if (await ShipOrder(item.OrderId, item.Shipment).Run(connection, token) is Error<ShipmentError>(var error))
                    return Result.Error<int, (int, ShipmentError)>((index, error));
            }
            return Result.Ok<int, (int, ShipmentError)>(shipments.Count);
        });

    // Pass LockShipping before a write, so concurrent changes to the same order wait for each other.
    private static AsyncReaderResult<NpgsqlConnection, OrderShipping, ShipmentError> Load(string query, long id) =>
        AsyncReaderResult.FromLookup<NpgsqlConnection, OrderShipping, ShipmentError>(
            async (connection, token) => Option.FromNullable((await connection.ReadAsync<ShippingRow>(token, query, new { id })).SingleOrDefault())
                .Map(row => new OrderShipping(
                    Option.FromNullable(row.Carrier).Zip(Option.FromNullable(row.Tracking), (carrier, tracking) => new Shipment(carrier, tracking)),
                    row.Delivered)),
            () => ShipmentError.OrderNotFound);

    private static AsyncReaderResult<NpgsqlConnection, Shipment, ShipmentError> Save(long id, Writer<Shipment, string> shipped) =>
        AsyncReaderResult.FromTask<NpgsqlConnection, Shipment, ShipmentError>(async (connection, token) =>
        {
            var (shipment, events) = shipped;
            await connection.ExecuteAsync(token, "UPDATE orders SET carrier = @Carrier, tracking = @Tracking WHERE id = @Id",
                new { Id = id, shipment.Carrier, shipment.Tracking });
            foreach (var kind in events)
                await Record(connection, id, kind, token);
            return shipment;
        });

    private static AsyncReaderResult<NpgsqlConnection, long, ShipmentError> Change(long id, string update, string kind) =>
        AsyncReaderResult.FromTask<NpgsqlConnection, long, ShipmentError>(async (connection, token) =>
        {
            await connection.ExecuteAsync(token, update, new { id });
            await Record(connection, id, kind, token);
            return id;
        });

    private static Task<int> Record(NpgsqlConnection connection, long id, string kind, CancellationToken token) =>
        connection.ExecuteAsync(token, "INSERT INTO order_events (order_id, kind) VALUES (@Id, @Kind)", new { Id = id, Kind = kind });

    private static IResult Problem(ShipmentError error) => Problem(error, null);

    private static IResult Problem(ShipmentError error, int? index) => error switch
    {
        ShipmentError.OrderNotFound => ApiResults.Problem(StatusCodes.Status404NotFound, "order-not-found", index),
        ShipmentError.ShipmentNotFound => ApiResults.Problem(StatusCodes.Status404NotFound, "shipment-not-found", index),
        ShipmentError.AlreadyDelivered => ApiResults.Problem(StatusCodes.Status409Conflict, "shipment-delivered", index),
        _ => throw new UnreachableException(),
    };
}
