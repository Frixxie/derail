using System.Diagnostics;
using DbReader;
using Derail;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Orders;

internal sealed record OrderRequest(string? Description, IReadOnlyList<OrderLineRequest?>? Lines);
internal sealed record OrderLineRequest(string? Sku, int? Quantity);
internal sealed record NewOrder(string Description, NonEmpty<NewOrderLine> Lines);
internal sealed record NewOrderLine(string Sku, int Quantity);
internal sealed record Order(long Id, string Description, IEnumerable<OrderLine> Lines);
internal sealed record OrderLine(long Id, string Sku, int Quantity);
internal sealed record OrderPage(int Limit, long After, Option<bool> Shipped);
internal sealed record OrderList(IReadOnlyList<Order> Items, long? Next);
internal enum OrderError { NotFound, KeyReused, Delivered }

internal static class OrderEndpoints
{
    public static Task<IResult> List(string? limit, string? after, string? shipped, NpgsqlDataSource db, CancellationToken token) =>
        Page(limit, after, shipped).OnValid(async page =>
        {
            var query = page.Shipped.Match(isShipped => isShipped ? ShippedOrders : UnshippedOrders, () => AllOrders);
            await using var connection = await db.OpenConnectionAsync(token);
            var items = (await connection.ReadAsync<Order>(token, query, new { page.After, page.Limit })).ToList();
            // A full page may have more after it; a short page is the last one.
            return Results.Ok(new OrderList(items, items.Count == page.Limit ? items[^1].Id : null));
        });

    public static Task<IResult> Get(long id, NpgsqlDataSource db, CancellationToken token) =>
        Find(id).InTransaction(db, token).Match(order => Results.Ok(order), Problem);

    public static Task<IResult> Create(OrderRequest request, [FromHeader(Name = "Idempotency-Key")] string? key,
        NpgsqlDataSource db, CancellationToken token) =>
        Validate(request).OnValid(order => Insert(order, key)
            .Recover(usedKey => Replay(usedKey, order))
            .Bind(Find)
            .InTransaction(db, token)
            .Match(created => Results.Created($"/orders/{created.Id}", created), Problem));

    public static Task<IResult> Update(long id, OrderRequest request, NpgsqlDataSource db, CancellationToken token) =>
        Validate(request).OnValid(order => Replace(id, order)
            .Bind(Find)
            .InTransaction(db, token)
            .Match(updated => Results.Ok(updated), Problem));

    public static async Task<IResult> Delete(long id, NpgsqlDataSource db, CancellationToken token)
    {
        await using var connection = await db.OpenConnectionAsync(token);
        var count = await connection.ExecuteAsync(token, "DELETE FROM orders WHERE id = @Id", new { id });
        return count == 1 ? Results.NoContent() : ApiResults.NotFound("order-not-found");
    }

    private static Validation<OrderPage, FieldError> Page(string? limit, string? after, string? shipped) =>
        ApiInput.Optional<int>(limit, "limit", int.TryParse)
            .Map(value => value.GetValueOrDefault(50))
            .Ensure(value => value is >= 1 and <= 100, _ => new("limit", "out-of-range"))
            .ToValidation()
            .Zip(
                ApiInput.Optional<long>(after, "after", long.TryParse).Map(value => value.GetValueOrDefault(0)).ToValidation(),
                ApiInput.Optional<bool>(shipped, "shipped", bool.TryParse).ToValidation(),
                (pageLimit, pageAfter, pageShipped) => new OrderPage(pageLimit, pageAfter, pageShipped));

    private static Validation<NewOrder, FieldError> Validate(OrderRequest request) =>
        ApiInput.Field(request.Description, "description", 200)
            .Zip(Lines(request.Lines ?? []), (description, lines) => new NewOrder(description, lines));

    private static Validation<NonEmpty<NewOrderLine>, FieldError> Lines(IReadOnlyList<OrderLineRequest?> lines) =>
        NonEmpty.FromEnumerable(lines.Index()).Match(
            requested => Validation.Traverse(requested, line => Line(line.Item, line.Index)),
            () => Validation.Invalid<NonEmpty<NewOrderLine>, FieldError>(new("lines", "required")));

    private static Validation<NewOrderLine, FieldError> Line(OrderLineRequest? line, int index) =>
        ApiInput.Field(line?.Sku, "sku", 40)
            .Zip(ApiInput.Positive(line?.Quantity, "quantity"), (sku, quantity) => new NewOrderLine(sku, quantity))
            .MapErrors(error => error with { Field = $"lines[{index}].{error.Field}" });

    // Every query is a compile-time constant; inputs are always parameters. DbReader maps the lines_* columns into Order.Lines.
    private const string Columns = "SELECT o.id, o.description, l.id AS lines_id, l.sku AS lines_sku, l.quantity AS lines_quantity";
    private const string WithLines = "LEFT JOIN order_lines l ON l.order_id = o.id ORDER BY o.id, l.id";
    private const string OrderById = $"{Columns} FROM (SELECT * FROM orders WHERE id = @Id) o {WithLines}";
    private const string OrderByKey = $"{Columns} FROM (SELECT * FROM orders WHERE idempotency_key = @Key) o {WithLines}";
    private const string AllOrders = $"{Columns} FROM (SELECT * FROM orders WHERE id > @After ORDER BY id LIMIT @Limit) o {WithLines}";
    private const string ShippedOrders =
        $"{Columns} FROM (SELECT * FROM orders WHERE id > @After AND carrier IS NOT NULL ORDER BY id LIMIT @Limit) o {WithLines}";
    private const string UnshippedOrders =
        $"{Columns} FROM (SELECT * FROM orders WHERE id > @After AND carrier IS NULL ORDER BY id LIMIT @Limit) o {WithLines}";

    private static AsyncReaderResult<NpgsqlConnection, Order, OrderError> Find(long id) =>
        FindOne(OrderById, new { id });

    private static AsyncReaderResult<NpgsqlConnection, Order, OrderError> FindOne(string query, object arguments) =>
        AsyncReaderResult.FromLookup<NpgsqlConnection, Order, OrderError>(
            async (connection, token) => Option.FromNullable((await connection.ReadAsync<Order>(token, query, arguments)).SingleOrDefault()),
            () => OrderError.NotFound);

    // Fails with the idempotency key when an order already uses it.
    private static AsyncReaderResult<NpgsqlConnection, long, string> Insert(NewOrder order, string? key) =>
        new(async (connection, token) =>
        {
            var ids = (await connection.ReadAsync<long>(token, """
                INSERT INTO orders (description, idempotency_key) VALUES (@Description, @Key)
                ON CONFLICT (idempotency_key) DO NOTHING RETURNING id
                """, new { order.Description, key })).ToList();
            // Only a supplied key can conflict.
            if (ids.Count == 0) return Result.Error<long, string>(key!);
            await InsertLines(connection, ids[0], order.Lines, token);
            return Result.Ok<long, string>(ids[0]);
        });

    // A retry with the same key and order gets the original order; a different order is rejected.
    private static AsyncReaderResult<NpgsqlConnection, long, OrderError> Replay(string key, NewOrder order) =>
        FindOne(OrderByKey, new { key })
            .Ensure(existing => existing.Description == order.Description
                    && existing.Lines.Select(line => new NewOrderLine(line.Sku, line.Quantity)).SequenceEqual(order.Lines),
                _ => OrderError.KeyReused)
            .Map(existing => existing.Id);

    // A delivered order is final, so its description and lines can no longer change.
    private static AsyncReaderResult<NpgsqlConnection, long, OrderError> Replace(long id, NewOrder order) =>
        AsyncReaderResult.FromLookup<NpgsqlConnection, bool, OrderError>(
                async (connection, token) => Option.FromNullable<bool>(
                    (await connection.ReadAsync<bool?>(token, "SELECT delivered FROM orders WHERE id = @Id FOR UPDATE", new { id })).SingleOrDefault()),
                () => OrderError.NotFound)
            .Ensure(delivered => !delivered, _ => OrderError.Delivered)
            .Bind(_ => AsyncReaderResult.FromTask<NpgsqlConnection, long, OrderError>(async (connection, token) =>
            {
                await connection.ExecuteAsync(token, "UPDATE orders SET description = @Description WHERE id = @Id", new { id, order.Description });
                await connection.ExecuteAsync(token, "DELETE FROM order_lines WHERE order_id = @Id", new { id });
                await InsertLines(connection, id, order.Lines, token);
                return id;
            }));

    private static async Task InsertLines(NpgsqlConnection connection, long orderId, NonEmpty<NewOrderLine> lines, CancellationToken token)
    {
        foreach (var line in lines)
            await connection.ExecuteAsync(token, "INSERT INTO order_lines (order_id, sku, quantity) VALUES (@OrderId, @Sku, @Quantity)",
                new { orderId, line.Sku, line.Quantity });
    }

    private static IResult Problem(OrderError error) => error switch
    {
        OrderError.NotFound => ApiResults.NotFound("order-not-found"),
        OrderError.KeyReused => ApiResults.Problem(StatusCodes.Status422UnprocessableEntity, "idempotency-key-reused"),
        OrderError.Delivered => ApiResults.Problem(StatusCodes.Status409Conflict, "order-delivered"),
        _ => throw new UnreachableException(),
    };
}
