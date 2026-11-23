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

if ((await discounted).GetValueOrDefault(-1) != 14 || quote.Run(2) != 16)
    throw new InvalidOperationException("Unexpected result from the packaged API.");
