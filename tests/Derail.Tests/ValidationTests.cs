using AwesomeAssertions;
using Derail;
using Xunit;

namespace Derail.Tests;

public class ValidationTests
{
    private static Validation<int, string> Parse(string input) =>
        int.TryParse(input, out var value)
            ? Validation.Valid<int, string>(value)
            : Validation.Invalid<int, string>($"'{input}' is not a number");

    private static readonly Validation<int, string> Failed = Validation.Invalid<int, string>("boom");

    public class Construction
    {
        [Fact]
        public void Valid_WrapsValue() =>
            (Validation.Valid<int, string>(1) is Valid<int>(1)).Should().BeTrue();

        [Fact]
        public void Invalid_WrapsErrors()
        {
            var validation = Validation.Invalid<int, string>("a", "b");

            (validation is Invalid<string>(var errors) && errors.SequenceEqual(["a", "b"])).Should().BeTrue();
        }

        [Fact]
        public void Cases_ConvertImplicitlyToValidation()
        {
            Validation<int, string> valid = new Valid<int>(1);
            Validation<int, string> invalid = new Invalid<string>("e");

            valid.IsValid.Should().BeTrue();
            invalid.IsInvalid.Should().BeTrue();
        }

        [Fact]
        public void DefaultInvalid_HasNoErrors() =>
            default(Invalid<string>).Errors.Should().BeEmpty();
    }

    public class Equality
    {
        [Fact]
        public void ValidWithEqualValues_AreEqual() =>
            Validation.Valid<int, string>(1).Should().Be(Validation.Valid<int, string>(1));

        [Fact]
        public void InvalidWithEqualErrors_AreEqual() =>
            Validation.Invalid<int, string>("a", "b").Should().Be(Validation.Invalid<int, string>("a", "b"));

        [Fact]
        public void InvalidWithErrorsInDifferentOrder_AreNotEqual() =>
            Validation.Invalid<int, string>("a", "b").Should().NotBe(Validation.Invalid<int, string>("b", "a"));

        [Fact]
        public void EqualInvalids_HaveEqualHashCodes() =>
            new Invalid<string>("a", "b").GetHashCode().Should().Be(new Invalid<string>("a", "b").GetHashCode());
    }

    public class Match
    {
        [Fact]
        public void CallsValidBranch_WhenValid() =>
            Parse("2").Match(v => v * 10, _ => -1).Should().Be(20);

        [Fact]
        public void CallsInvalidBranch_WithAllErrors() =>
            Validation.Invalid<int, string>("a", "b").Match(_ => "", e => string.Join(",", e)).Should().Be("a,b");
    }

    public class Map
    {
        [Fact]
        public void TransformsValidValue() =>
            Parse("2").Map(x => x * 2).Should().Be(Validation.Valid<int, string>(4));

        [Fact]
        public void KeepsErrors() =>
            Failed.Map(x => x * 2).Should().Be(Failed);
    }

    public class MapErrors
    {
        [Fact]
        public void TransformsEveryError() =>
            Validation.Invalid<int, string>("a", "bb").MapErrors(e => e.Length)
                .Should().Be(Validation.Invalid<int, int>(1, 2));

        [Fact]
        public void KeepsValid() =>
            Parse("1").MapErrors(e => e.Length).Should().Be(Validation.Valid<int, int>(1));
    }

    public class Zip
    {
        [Fact]
        public void CombinesValidValues() =>
            Parse("1").Zip(Parse("2"), (a, b) => a + b).Should().Be(Validation.Valid<int, string>(3));

        [Fact]
        public void AccumulatesErrors_FromBothSides() =>
            Parse("x").Zip(Parse("y"), (a, b) => a + b)
                .Should().Be(Validation.Invalid<int, string>("'x' is not a number", "'y' is not a number"));

        [Fact]
        public void KeepsErrors_WhenOnlyOneSideIsInvalid()
        {
            Parse("1").Zip(Parse("y"), (a, b) => a + b).Should().Be(Validation.Invalid<int, string>("'y' is not a number"));
            Parse("x").Zip(Parse("2"), (a, b) => a + b).Should().Be(Validation.Invalid<int, string>("'x' is not a number"));
        }

        [Fact]
        public void ThreeWay_AccumulatesErrorsInOrder() =>
            Parse("x").Zip(Parse("2"), Parse("z"), (a, b, c) => a + b + c)
                .Should().Be(Validation.Invalid<int, string>("'x' is not a number", "'z' is not a number"));

        [Fact]
        public void ThreeWay_CombinesValidValues() =>
            Parse("1").Zip(Parse("2"), Parse("3"), (a, b, c) => a + b + c)
                .Should().Be(Validation.Valid<int, string>(6));
    }

    public class Apply
    {
        private static readonly Func<int, int> Increment = x => x + 1;

        [Fact]
        public void AppliesValidFunction_ToValidValue() =>
            Validation.Valid<Func<int, int>, string>(Increment).Apply(Parse("1"))
                .Should().Be(Validation.Valid<int, string>(2));

        [Fact]
        public void AccumulatesErrors_FromFunctionAndValue() =>
            Validation.Invalid<Func<int, int>, string>("no function").Apply(Parse("x"))
                .Should().Be(Validation.Invalid<int, string>("no function", "'x' is not a number"));

        [Fact]
        public void CurriedFunction_CollectsEveryError()
        {
            Func<int, Func<int, int>> add = a => b => a + b;

            Validation.Valid<Func<int, Func<int, int>>, string>(add)
                .Apply(Parse("x"))
                .Apply(Parse("y"))
                .Should().Be(Validation.Invalid<int, string>("'x' is not a number", "'y' is not a number"));
        }

        [Theory]
        [InlineData("1")]
        [InlineData("x")]
        public void Identity(string input) =>
            Validation.Valid<Func<int, int>, string>(x => x).Apply(Parse(input)).Should().Be(Parse(input));

        [Fact]
        public void Homomorphism() =>
            Validation.Valid<Func<int, int>, string>(Increment).Apply(Validation.Valid<int, string>(1))
                .Should().Be(Validation.Valid<int, string>(Increment(1)));
    }

    public class Sequence
    {
        [Fact]
        public void CollectsValidValues() =>
            Validation.Sequence([Parse("1"), Parse("2")]).Match(v => string.Join(",", v), _ => "")
                .Should().Be("1,2");

        [Fact]
        public void EmptyInput_IsValid() =>
            Validation.Sequence(Array.Empty<Validation<int, string>>()).Match(v => v.Count, _ => -1)
                .Should().Be(0);

        [Fact]
        public void AccumulatesAllErrors() =>
            Validation.Sequence([Parse("x"), Parse("1"), Parse("y")]).Match(_ => "", e => string.Join(",", e))
                .Should().Be("'x' is not a number,'y' is not a number");

        [Fact]
        public void DefaultInvalid_RemainsInvalidEvenWithoutErrors()
        {
            Validation<int, string> invalid = default(Invalid<string>);
            var result = Validation.Sequence([Parse("1"), invalid, Parse("2")]);

            result.IsInvalid.Should().BeTrue();
            result.Match(_ => ["unexpected success"], errors => errors).Should().BeEmpty();
        }

        [Fact]
        public void EnumeratesOnceAndContinuesAfterAnInvalidItem()
        {
            var enumerations = 0;
            var visited = new List<int>();
            IEnumerable<Validation<int, string>> Source()
            {
                enumerations++;
                if (enumerations > 1) throw new InvalidOperationException("Enumerated twice");
                visited.Add(1);
                yield return Validation.Invalid<int, string>("first", "second");
                visited.Add(2);
                yield return Parse("2");
                visited.Add(3);
                yield return Validation.Invalid<int, string>("third");
            }

            var result = Validation.Sequence(Source());

            result.Match(_ => [], errors => errors).Should().Equal("first", "second", "third");
            visited.Should().Equal(1, 2, 3);
            enumerations.Should().Be(1);
        }

        [Fact]
        public void DefaultUnion_IsRejectedRatherThanSilentlySkipped()
        {
            var act = () => Validation.Sequence(new[] { default(Validation<int, string>) });

            act.Should().Throw<System.Runtime.CompilerServices.SwitchExpressionException>();
        }

        [Fact]
        public void ResultsAreIndependentSnapshots()
        {
            var items = new[] { Parse("1"), Parse("2") };
            var first = Validation.Sequence(items);
            items[0] = Parse("3");
            var second = Validation.Sequence(items);

            first.Match(values => values, _ => []).Should().Equal(1, 2);
            second.Match(values => values, _ => []).Should().Equal(3, 2);
        }
    }

    public class Conversions
    {
        [Fact]
        public void ValidBecomesOk() =>
            Parse("1").ToResult().Match(v => v, _ => -1).Should().Be(1);

        [Fact]
        public void InvalidBecomesErrorWithAllErrors() =>
            Validation.Invalid<int, string>("a", "b").ToResult().Match(_ => "", e => string.Join(",", e))
                .Should().Be("a,b");

        [Fact]
        public void GetValueOrDefault_ReturnsFallback_WhenInvalid() =>
            Failed.GetValueOrDefault(99).Should().Be(99);
    }
}
