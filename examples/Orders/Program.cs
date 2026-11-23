using Npgsql;
using Orders;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Orders")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__Orders for the example database.");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler();
var orders = app.MapGroup("/orders");
orders.MapPost("", OrderEndpoints.Create);
orders.MapGet("", OrderEndpoints.List);
orders.MapGet("/{id:long}", OrderEndpoints.Get);
orders.MapPut("/{id:long}", OrderEndpoints.Update);
orders.MapDelete("/{id:long}", OrderEndpoints.Delete);
var shipments = orders.MapGroup("/{id:long}/shipment");
shipments.MapPut("", ShipmentEndpoints.Put);
shipments.MapGet("", ShipmentEndpoints.Get);
shipments.MapDelete("", ShipmentEndpoints.Delete);
app.Run();
