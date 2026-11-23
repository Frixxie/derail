using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Orders.Tests;

/// <summary>A disposable PostgreSQL server with the example schema, and the API hosted in-process against it.</summary>
public sealed class OrdersApi : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private NpgsqlDataSource? _db;

    private HttpClient Client => _client ?? throw new InvalidOperationException("The API has not started.");
    private NpgsqlDataSource Db => _db ?? throw new InvalidOperationException("The database has not started.");

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await _postgres.StartAsync(token);
        _db = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await Execute(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"), token));

        // Production hides exception details in problem responses, as a deployed API would.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production")
            .UseSetting("Logging:LogLevel:Default", "Warning")
            .UseSetting("ConnectionStrings:Orders", _postgres.GetConnectionString()));
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        if (_db is not null) await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public Task<HttpResponseMessage> Ship(long id, object payload) =>
        Client.PutAsJsonAsync($"/orders/{id}/shipment", payload, TestContext.Current.CancellationToken);

    public async Task<(HttpStatusCode First, HttpStatusCode Second)> ShipTogether(long id, object first, object second)
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await Db.OpenConnectionAsync(token);
        await using var gate = await connection.BeginTransactionAsync(token);
        await using var hold = new NpgsqlCommand("SELECT id FROM orders WHERE id = $1 FOR UPDATE", connection, gate);
        hold.Parameters.AddWithValue(id);
        await hold.ExecuteNonQueryAsync(token);

        async Task<HttpStatusCode> Send(object shipment)
        {
            using var response = await Ship(id, shipment);
            return response.StatusCode;
        }

        var left = Send(first);
        var right = Send(second);
        // This fixture owns the database. Release the row only once both HTTP requests are waiting on a lock.
        await using var waiters = Db.CreateCommand("""
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND backend_type = 'client backend' AND wait_event_type = 'Lock'
            """);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while ((long)(await waiters.ExecuteScalarAsync(deadline.Token))! < 2)
            await Task.Delay(20, deadline.Token);

        await gate.RollbackAsync(token);
        return (await left, await right);
    }

    private NpgsqlCommand Command(string sql, object[] parameters)
    {
        var command = Db.CreateCommand(sql);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter);
        return command;
    }

    public async Task Execute(string sql, params object[] parameters)
    {
        await using var command = Command(sql, parameters);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async Task<T?> Scalar<T>(string sql, params object[] parameters)
    {
        await using var command = Command(sql, parameters);
        return (T?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
