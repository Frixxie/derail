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

    public async Task Execute(string sql)
    {
        await using var command = Db.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async Task<T?> Scalar<T>(string sql)
    {
        await using var command = Db.CreateCommand(sql);
        return (T?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
