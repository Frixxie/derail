using DbReader;
using Derail;
using Npgsql;

namespace Orders;

internal sealed record Order(long Id, string Description);
internal sealed record OrderRequest(string? Description);

internal static class OrderEndpoints
{
    public static async Task<IResult> List(NpgsqlDataSource db, CancellationToken token)
    {
        await using var connection = await db.OpenConnectionAsync(token);
        return Results.Ok(await connection.ReadAsync<Order>(token, "SELECT id, description FROM orders ORDER BY id"));
    }

    public static async Task<IResult> Get(long id, NpgsqlDataSource db, CancellationToken token)
    {
        await using var connection = await db.OpenConnectionAsync(token);
        var rows = await connection.ReadAsync<Order>(token, "SELECT id, description FROM orders WHERE id = @Id", new { id });
        return Option.FromNullable(rows.SingleOrDefault()).OkOrNotFound("order-not-found");
    }

    public static Task<IResult> Create(OrderRequest request, NpgsqlDataSource db, CancellationToken token) =>
        ApiInput.Field(request.Description, "description", 200).OnValid(async description =>
    {
        await using var connection = await db.OpenConnectionAsync(token);
        var rows = await connection.ReadAsync<Order>(token,
            "INSERT INTO orders (description) VALUES (@Description) RETURNING id, description", new { description });
        var order = rows.Single();
        return Results.Created($"/orders/{order.Id}", order);
    });

    public static Task<IResult> Update(long id, OrderRequest request, NpgsqlDataSource db, CancellationToken token) =>
        ApiInput.Field(request.Description, "description", 200).OnValid(async description =>
    {
        await using var connection = await db.OpenConnectionAsync(token);
        var rows = await connection.ReadAsync<Order>(token,
            "UPDATE orders SET description = @Description WHERE id = @Id RETURNING id, description", new { id, description });
        return Option.FromNullable(rows.SingleOrDefault()).OkOrNotFound("order-not-found");
    });

    public static async Task<IResult> Delete(long id, NpgsqlDataSource db, CancellationToken token)
    {
        await using var connection = await db.OpenConnectionAsync(token);
        var count = await connection.ExecuteAsync(token, "DELETE FROM orders WHERE id = @Id", new { id });
        return count == 1 ? Results.NoContent() : ApiResults.NotFound("order-not-found");
    }
}
