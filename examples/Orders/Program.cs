using Derail;
using Npgsql;
using Orders;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Orders")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__Orders for the example database.");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler();
app.MapPut("/orders/{id:long}/shipment", Ship);
app.Run();

static async Task<IResult> Ship(long id, ShipmentRequest request, NpgsqlDataSource db, CancellationToken token)
{
    switch (Shipping.Validate(request))
    {
        case Invalid<FieldError>(var errors):
            return Results.ValidationProblem(errors.GroupBy(e => e.Field)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Code).ToArray()));
        case Valid<Shipment>(var shipment):
            var result = await OrderStore.Ship(id, shipment).Run(db, token);
            return result.Match(value => Results.Ok(value), error => error switch
            {
                OrderNotFound => Problem(404, "order-not-found"),
                AlreadyShipped => Problem(409, "already-shipped"),
            });
        default: throw new InvalidOperationException("Uninitialized validation");
    }
}

static IResult Problem(int status, string code) => Results.Problem(statusCode: status,
    extensions: new Dictionary<string, object?> { ["code"] = code });
