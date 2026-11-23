using Derail;

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

if ((await discounted).GetValueOrDefault(-1) != 14 || quote.Run(2) != 16 || (await observed).GetValueOrDefault(-1) != 4)
    throw new InvalidOperationException("Unexpected result from the packaged API.");
