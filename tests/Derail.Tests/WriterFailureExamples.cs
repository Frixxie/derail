using Xunit;

namespace Derail.Tests;

public class WriterFailureExamples
{
    private enum Step { StockChecked, ReservationProposed }
    private enum ReservationError { OutOfStock }

    // Both outcomes retain the stock check. Only a proposed reservation adds a second event.
    private static Writer<Result<int, ReservationError>, Step> ReserveWithTrace(int available) =>
        new Writer<int, Step>(available, [Step.StockChecked]).Bind(stock => stock > 0
            ? new Writer<Result<int, ReservationError>, Step>(Result.Ok<int, ReservationError>(stock - 1), [Step.ReservationProposed])
            : Writer.Pure<Result<int, ReservationError>, Step>(Result.Error<int, ReservationError>(ReservationError.OutOfStock)));

    // Choose a success-only proposal at the boundary, without running the reservation again.
    private static Result<Writer<int, Step>, ReservationError> ReserveOnSuccess(int available)
    {
        var trace = ReserveWithTrace(available);
        return trace.Value.Map(remaining => trace.Map(_ => remaining));
    }

    [Fact]
    public void Successful_reservation_returns_the_same_value_and_ordered_events_in_both_shapes()
    {
        var trace = ReserveWithTrace(3);
        Assert.Equal(Result.Ok<int, ReservationError>(2), trace.Value);
        Assert.Equal([Step.StockChecked, Step.ReservationProposed], trace.Events);

        var proposal = ReserveOnSuccess(3).Match(writer => writer, error => throw new InvalidOperationException(error.ToString()));
        Assert.Equal(2, proposal.Value);
        Assert.Equal([Step.StockChecked, Step.ReservationProposed], proposal.Events);
    }

    [Fact]
    public void Out_of_stock_retains_the_earlier_check_only_when_writer_is_outermost()
    {
        var trace = ReserveWithTrace(0);
        Assert.Equal(Result.Error<int, ReservationError>(ReservationError.OutOfStock), trace.Value);
        Assert.Equal([Step.StockChecked], trace.Events);

        Assert.Equal(Result.Error<Writer<int, Step>, ReservationError>(ReservationError.OutOfStock), ReserveOnSuccess(0));
    }
}
