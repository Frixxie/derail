using Xunit;

namespace Derail.Tests.Examples;

public class ShippingExamplesTests
{
    [Fact]
    public void Parcel_collects_invalid_fields_in_constructor_order()
    {
        Assert.True(Parcel.Create(30, 20, 10, 1.5m) is Valid<Parcel>(Parcel(30, 20, 10, 1.5m)));
        Assert.True(Parcel.Create(0, 20, 10, -1m).Match(_ => false,
            errors => errors.SequenceEqual(["lengthCm", "weightKg"])));
    }

}
