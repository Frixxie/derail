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
        Validate(request.Carrier, request.Tracking).OnValid(shipment => db
            .InTransaction((connection, ct) => ShipOrder(connection, id, shipment, ct), token)
            .Match(saved => Results.Ok(saved), Problem));

    public static Task<IResult> Import(IReadOnlyList<ShipmentImportRow?> rows, NpgsqlDataSource db, CancellationToken token) =>
        NonEmpty.FromEnumerable(rows.Index())
            .Match(
                requested => Validation.Traverse(requested, row => ImportRow(row.Item)
                    .MapErrors(error => error with { Field = $"[{row.Index}].{error.Field}" })),
                () => Validation.Invalid<NonEmpty<OrderShipment>, FieldError>(new("shipments", "required")))
            .OnValid(shipments => db
                .InTransaction((connection, ct) => ShipAll(connection, shipments, ct), token)
                .Match(_ => Results.NoContent(), failure => Problem(failure.Error, failure.Index)));

    public static Task<IResult> Get(long id, NpgsqlDataSource db, CancellationToken token) =>
        db.InTransaction((connection, ct) => Load(connection, ReadShipping, id, ct)
                .Bind(order => order.Shipment
                    .Map(shipment => new ShipmentStatus(shipment.Carrier, shipment.Tracking, order.Delivered))
                    .ToResult(() => ShipmentError.ShipmentNotFound)), token)
            .Match(status => Results.Ok(status), Problem);

    public static Task<IResult> Delete(long id, NpgsqlDataSource db, CancellationToken token) =>
        db.InTransaction((connection, ct) => Load(connection, LockShipping, id, ct)
                .Bind(order => Result.Ok<OrderShipping, ShipmentError>(order)
                    .Ensure(o => o.Shipment.IsSome, _ => ShipmentError.ShipmentNotFound)
                    .Ensure(o => !o.Delivered, _ => ShipmentError.AlreadyDelivered))
                .MapAsync(_ => Change(connection, id, "UPDATE orders SET carrier = NULL, tracking = NULL WHERE id = @Id", "shipment-removed", ct)), token)
            .Match(_ => Results.NoContent(), Problem);

    // Repeating a delivery succeeds without recording a second event.
    public static Task<IResult> Deliver(long id, NpgsqlDataSource db, CancellationToken token) =>
        db.InTransaction((connection, ct) => Load(connection, LockShipping, id, ct)
                .Bind(order => Result.Ok<OrderShipping, ShipmentError>(order)
                    .Ensure(o => o.Shipment.IsSome, _ => ShipmentError.ShipmentNotFound))
                .MapAsync(order => order.Delivered
                    ? Task.FromResult(id)
                    : Change(connection, id, "UPDATE orders SET delivered = true WHERE id = @Id", "shipment-delivered", ct)), token)
            .Match(_ => Results.NoContent(), Problem);

    private static Validation<Shipment, FieldError> Validate(string? carrier, string? tracking) =>
        ApiInput.Field(carrier, "carrier", 40)
            .Zip(ApiInput.Field(tracking, "tracking", 80), (validCarrier, validTracking) => new Shipment(validCarrier, validTracking));

    private static Validation<OrderShipment, FieldError> ImportRow(ShipmentImportRow? row) =>
        ApiInput.Positive(row?.OrderId, "orderId")
            .Zip(Validate(row?.Carrier, row?.Tracking), (orderId, shipment) => new OrderShipment(orderId, shipment));

    private static Task<Result<Shipment, ShipmentError>> ShipOrder(
        NpgsqlConnection connection, long id, Shipment shipment, CancellationToken token) =>
        Load(connection, LockShipping, id, token)
            .Bind(order => Result.Ok<OrderShipping, ShipmentError>(order)
                .Ensure(o => !o.Delivered, _ => ShipmentError.AlreadyDelivered))
            .Map(order => Shipping.Ship(order.Shipment, shipment))
            .MapAsync(shipped => Save(connection, id, shipped, token));

    private static async Task<Result<int, (int Index, ShipmentError Error)>> ShipAll(
        NpgsqlConnection connection, NonEmpty<OrderShipment> shipments, CancellationToken token)
    {
        foreach (var (index, item) in shipments.Index())
        {
            if (await ShipOrder(connection, item.OrderId, item.Shipment, token) is Error<ShipmentError>(var error))
                return Result.Error<int, (int, ShipmentError)>((index, error));
        }
        return Result.Ok<int, (int, ShipmentError)>(shipments.Count);
    }

    // Pass LockShipping before a write, so concurrent changes to the same order wait for each other.
    private static async Task<Result<OrderShipping, ShipmentError>> Load(
        NpgsqlConnection connection, string query, long id, CancellationToken token) =>
        Option.FromNullable((await connection.ReadAsync<ShippingRow>(token, query, new { id })).SingleOrDefault())
            .Map(row => new OrderShipping(
                Option.FromNullable(row.Carrier).Zip(Option.FromNullable(row.Tracking), (carrier, tracking) => new Shipment(carrier, tracking)),
                row.Delivered))
            .ToResult(() => ShipmentError.OrderNotFound);

    private static async Task<Shipment> Save(NpgsqlConnection connection, long id, Shipped shipped, CancellationToken token)
    {
        var (shipment, events) = shipped;
        await connection.ExecuteAsync(token, "UPDATE orders SET carrier = @Carrier, tracking = @Tracking WHERE id = @Id",
            new { Id = id, shipment.Carrier, shipment.Tracking });
        foreach (var kind in events)
            await Record(connection, id, kind, token);
        return shipment;
    }

    private static async Task<long> Change(NpgsqlConnection connection, long id, string update, string kind, CancellationToken token)
    {
        await connection.ExecuteAsync(token, update, new { id });
        await Record(connection, id, kind, token);
        return id;
    }

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
