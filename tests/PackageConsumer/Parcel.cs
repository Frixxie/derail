using Derail;

namespace PackageConsumer;

internal sealed record Parcel(int LengthCm, int WidthCm, int HeightCm, decimal WeightKg)
{
    public static Validation<Parcel, string> Create(int lengthCm, int widthCm, int heightCm, decimal weightKg)
    {
        var length = Dimension(lengthCm, "lengthCm");
        var width = Dimension(widthCm, "widthCm");
        var height = Dimension(heightCm, "heightCm");
        var weight = weightKg > 0 ? Validation.Valid<decimal, string>(weightKg) : Validation.Invalid<decimal, string>("weightKg");
        return length.Zip(width, height, weight, (l, w, h, kg) => new Parcel(l, w, h, kg));
    }

    private static Validation<int, string> Dimension(int cm, string field) =>
        cm > 0 ? Validation.Valid<int, string>(cm) : Validation.Invalid<int, string>(field);
}
