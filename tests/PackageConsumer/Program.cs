using Derail;
using PackageConsumer;

var quantity = Option.Some(3).ToResult(() => "missing quantity");

// An untyped async lambda must still select the Task overload.
Task<Result<int, string>> total = quantity.MapAsync(async count =>
{
    await Task.Yield();
    return count * 5;
});
ValueTask<Result<int, string>> discounted = (await total).MapAsync(price => ValueTask.FromResult(price - 1));

var quote = from fee in Reader.Ask<int>()
            from price in Reader.Pure<int, int>(14)
            select price + fee;

// Recovery infers the replacement error type; plain async lambdas select Task.
Task<Option<int>> alternative = default(Option<int>).OrElseAsync(async () =>
{
    await Task.Yield();
    return Option.Some(4);
});
Task<Result<int, int>> recovered = Result.Error<int, string>("missing").RecoverAsync(async _ =>
    (await alternative).ToResult(() => 404));
Task<Result<int, int>> observed = (await recovered).TapErrorAsync(async _ => await Task.Yield());

var workflow = from fee in AsyncReaderResult.Ask<int, string>()
               from count in AsyncReaderResult.FromResult<int, int, string>(quantity)
               select count + fee;
if (!(await workflow.Run(2)).Equals(Result.Ok<int, string>(5)))
    throw new InvalidOperationException("Unexpected result from the packaged async reader.");

if ((await discounted).GetValueOrDefault(-1) != 14 || quote.Run(2) != 16 || (await observed).GetValueOrDefault(-1) != 4)
    throw new InvalidOperationException("Unexpected result from the packaged API.");

if (Parcel.Create(30, 20, 10, 1.5m) is not Valid<Parcel>(Parcel(30, 20, 10, 1.5m)) ||
    !Parcel.Create(0, 20, 10, -1m).Match(_ => false, errors => errors.SequenceEqual(["lengthCm", "weightKg"])))
    throw new InvalidOperationException("Unexpected parcel validation result.");

var subtotal = Result.Ok<Writer<decimal, string>, string>(new(20m, ["priced", "discounted"]));
var shipping = AsyncReaderResult.FromReaderResult(ShippingQuote.WithFreight(subtotal, "Parcel"));
var priced = await shipping.Run(new RateTable(new Dictionary<string, decimal> { ["Parcel"] = 3m }));
var missingRate = await shipping.Run(new RateTable(new Dictionary<string, decimal>()));
if (!priced.Match(w => w.Value == 23m && w.Events.SequenceEqual(["priced", "discounted"]), _ => false)
    || !missingRate.Equals(Result.Error<Writer<decimal, string>, string>("No rate for Parcel")))
    throw new InvalidOperationException("Unexpected shipping quote after lifting the workflow.");
