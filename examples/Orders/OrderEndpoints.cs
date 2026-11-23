using System.Diagnostics;
using DbReader;
using Derail;
using Npgsql;

namespace Orders;

internal sealed record OrderRequest(string? Description, IReadOnlyList<OrderLineRequest?>? Lines);
internal sealed record OrderLineRequest(string? Sku, int? Quantity);
internal sealed record NewOrder(string Description, NonEmpty<NewOrderLine> Lines);
internal sealed record NewOrderLine(string Sku, int Quantity);
internal sealed record Order(long Id, string Description, IEnumerable<OrderLine> Lines);
internal sealed record OrderLine(long Id, string Sku, int Quantity);
internal sealed record OrderPage(int Limit, long After, Option<bool> Shipped);
internal enum OrderError { NotFound }

internal static class OrderEndpoints
{
    public static Task<IResult> List(string? limit, string? after, string? shipped, NpgsqlDataSource db, CancellationToken token) =>
        Page(limit, after, shipped).OnValid(async page =>
        {
            var filter = page.Shipped.Match(isShipped => isShipped ? "AND carrier IS NOT NULL" : "AND carrier IS NULL", () => "");
            await using var connection = await db.OpenConnectionAsync(token);
            return Results.Ok(await connection.ReadAsync<Order>(token,
                SelectOrders($"SELECT * FROM orders WHERE id > @After {filter} ORDER BY id LIMIT @Limit"), new { page.After, page.Limit }));
        });

    public static Task<IResult> Get(long id, NpgsqlDataSource db, CancellationToken token) =>
        Find(id).InTransaction(db, token).Match(order => Results.Ok(order), Problem);

    public static Task<IResult> Create(OrderRequest request, NpgsqlDataSource db, CancellationToken token) =>
        Validate(request).OnValid(order => Insert(order)
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
        NonEmpty.FromEnumerable(lines).Match(
            requested => Line(requested.Head, 0).Zip(
                Validation.Traverse(requested.Tail.Index(), line => Line(line.Item, line.Index + 1)),
                (head, tail) => new NonEmpty<NewOrderLine>(head, [.. tail])),
            () => Validation.Invalid<NonEmpty<NewOrderLine>, FieldError>(new("lines", "required")));

    private static Validation<NewOrderLine, FieldError> Line(OrderLineRequest? line, int index) =>
        ApiInput.Field(line?.Sku, "sku", 40)
            .Zip(ApiInput.Positive(line?.Quantity, "quantity"), (sku, quantity) => new NewOrderLine(sku, quantity))
            .MapErrors(error => error with { Field = $"lines[{index}].{error.Field}" });

    // DbReader maps the lines_* columns into Order.Lines.
    private static string SelectOrders(string orders) => $"""
        SELECT o.id, o.description, l.id AS lines_id, l.sku AS lines_sku, l.quantity AS lines_quantity
        FROM ({orders}) o LEFT JOIN order_lines l ON l.order_id = o.id
        ORDER BY o.id, l.id
        """;

    private static AsyncReaderResult<NpgsqlConnection, Order, OrderError> Find(long id) =>
        new(async (connection, token) =>
        {
            var orders = await connection.ReadAsync<Order>(token, SelectOrders("SELECT * FROM orders WHERE id = @Id"), new { id });
            return Option.FromNullable(orders.SingleOrDefault()).ToResult(() => OrderError.NotFound);
        });

    private static AsyncReaderResult<NpgsqlConnection, long, OrderError> Insert(NewOrder order) =>
        new(async (connection, token) =>
        {
            var ids = await connection.ReadAsync<long>(token,
                "INSERT INTO orders (description) VALUES (@Description) RETURNING id", new { order.Description });
            var id = ids.Single();
            await InsertLines(connection, id, order.Lines, token);
            return Result.Ok<long, OrderError>(id);
        });

    private static AsyncReaderResult<NpgsqlConnection, long, OrderError> Replace(long id, NewOrder order) =>
        new(async (connection, token) =>
        {
            var count = await connection.ExecuteAsync(token,
                "UPDATE orders SET description = @Description WHERE id = @Id", new { id, order.Description });
            if (count == 0) return Result.Error<long, OrderError>(OrderError.NotFound);
            await connection.ExecuteAsync(token, "DELETE FROM order_lines WHERE order_id = @Id", new { id });
            await InsertLines(connection, id, order.Lines, token);
            return Result.Ok<long, OrderError>(id);
        });

    private static async Task InsertLines(NpgsqlConnection connection, long orderId, NonEmpty<NewOrderLine> lines, CancellationToken token)
    {
        foreach (var line in lines)
            await connection.ExecuteAsync(token, "INSERT INTO order_lines (order_id, sku, quantity) VALUES (@OrderId, @Sku, @Quantity)",
                new { orderId, line.Sku, line.Quantity });
    }

    private static IResult Problem(OrderError error) => error switch
    {
        OrderError.NotFound => ApiResults.NotFound("order-not-found"),
        _ => throw new UnreachableException(),
    };
}
