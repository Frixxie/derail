using Derail;

namespace Derail.Tests.Examples;

internal sealed record Parcel(int LengthCm, int WidthCm, int HeightCm, decimal WeightKg)
{
    public static Validation<Parcel, string> Create(int lengthCm, int widthCm, int heightCm, decimal weightKg)
    {
        var length = Dimension(lengthCm, "lengthCm");
        var width = Dimension(widthCm, "widthCm");
        var height = Dimension(heightCm, "heightCm");
        var weight = weightKg > 0 ? Validation.Valid<decimal, string>(weightKg) : Validation.Invalid<decimal, string>("weightKg");
        static Func<int, Func<int, Func<decimal, Parcel>>> WithLength(int l) => w => h => kg => new Parcel(l, w, h, kg);
        return length.Map(WithLength).Apply(width).Apply(height).Apply(weight);
    }

    private static Validation<int, string> Dimension(int cm, string field) =>
        cm > 0 ? Validation.Valid<int, string>(cm) : Validation.Invalid<int, string>(field);
}
