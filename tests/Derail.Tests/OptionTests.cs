using AwesomeAssertions;
using Derail;
using Xunit;

namespace Derail.Tests;

public class OptionTests
{
    private static readonly Option<int> NoInt = Option.None;

    public class Construction
    {
        [Fact]
        public void Some_WrapsValue() =>
            (Option.Some(1) is Some<int>(1)).Should().BeTrue();

        [Fact]
        public void None_ConvertsImplicitlyToAnyOption()
        {
            Option<string> text = Option.None;
            Option<int> number = new None();

            text.IsNone.Should().BeTrue();
            number.IsNone.Should().BeTrue();
        }

        [Fact]
        public void SomeCase_ConvertsImplicitlyToOption()
        {
            Option<int> option = new Some<int>(5);

            option.Should().Be(Option.Some(5));
        }

        [Fact]
        public void FromNullable_Reference_NullIsNone() =>
            Option.FromNullable<string>(null).IsNone.Should().BeTrue();

        [Fact]
        public void FromNullable_Reference_NonNullIsSome() =>
            Option.FromNullable("a").Should().Be(Option.Some("a"));

        [Fact]
        public void FromNullable_Value_NullIsNone() =>
            Option.FromNullable((int?)null).IsNone.Should().BeTrue();

        [Fact]
        public void FromNullable_Value_HasValueIsSome() =>
            Option.FromNullable((int?)1).Should().Be(Option.Some(1));
    }

    public class Equality
    {
        [Fact]
        public void SomeWithEqualValues_AreEqual() =>
            Option.Some("a").Should().Be(Option.Some("a"));

        [Fact]
        public void SomeWithDifferentValues_AreNotEqual() =>
            Option.Some("a").Should().NotBe(Option.Some("b"));

        [Fact]
        public void SomeAndNone_AreNotEqual() =>
            Option.Some(0).Should().NotBe(NoInt);
    }

    public class State
    {
        [Fact]
        public void IsSome_And_IsNone_ForSome()
        {
            var option = Option.Some(1);

            option.IsSome.Should().BeTrue();
            option.IsNone.Should().BeFalse();
        }

        [Fact]
        public void IsSome_And_IsNone_ForNone()
        {
            NoInt.IsSome.Should().BeFalse();
            NoInt.IsNone.Should().BeTrue();
        }

        [Fact]
        public void IsSome_And_IsNone_ForDefault()
        {
            Option<int> option = default;

            option.IsSome.Should().BeFalse();
            option.IsNone.Should().BeFalse();
        }

        [Fact]
        public void PatternMatching_ExtractsValue()
        {
            var description = Option.Some(42) switch
            {
                Some<int>(var v) when v > 10 => $"large {v}",
                Some<int>(var v) => $"small {v}",
                None => "nothing",
            };

            description.Should().Be("large 42");
        }
    }

    public class Match
    {
        [Fact]
        public void CallsSomeBranch_WhenPresent() =>
            Option.Some(2).Match(v => v * 10, () => -1).Should().Be(20);

        [Fact]
        public void CallsNoneBranch_WhenAbsent() =>
            NoInt.Match(v => v * 10, () => -1).Should().Be(-1);

        [Fact]
        public void DoesNotCallSomeBranch_WhenAbsent()
        {
            var called = false;

            NoInt.Match(v => { called = true; return v; }, () => 0);

            called.Should().BeFalse();
        }
    }

    public class Map
    {
        [Fact]
        public void TransformsPresentValue() =>
            Option.Some(2).Map(x => x * 2).Should().Be(Option.Some(4));

        [Fact]
        public void CanChangeType() =>
            Option.Some(2).Map(x => x.ToString()).Should().Be(Option.Some("2"));

        [Fact]
        public void KeepsNone() =>
            NoInt.Map(x => x * 2).IsNone.Should().BeTrue();

        [Fact]
        public void Identity_IsNoOp()
        {
            Option.Some(3).Map(x => x).Should().Be(Option.Some(3));
            NoInt.Map(x => x).Should().Be(NoInt);
        }

        [Fact]
        public void Composition_EqualsSequentialMaps()
        {
            Func<int, int> f = x => x + 1;
            Func<int, int> g = x => x * 2;

            Option.Some(3).Map(x => g(f(x))).Should().Be(Option.Some(3).Map(f).Map(g));
        }
    }

    public class Bind
    {
        private static Option<int> HalfIfEven(int x) => x % 2 == 0 ? Option.Some(x / 2) : Option.None;

        [Fact]
        public void ChainsWhenPresent() =>
            Option.Some(8).Bind(HalfIfEven).Bind(HalfIfEven).Should().Be(Option.Some(2));

        [Fact]
        public void ReturnsNone_WhenFunctionReturnsNone() =>
            Option.Some(3).Bind(HalfIfEven).IsNone.Should().BeTrue();

        [Fact]
        public void ShortCircuits_WhenAbsent()
        {
            var called = false;

            NoInt.Bind(x => { called = true; return Option.Some(x); });

            called.Should().BeFalse();
        }

        [Fact]
        public void LeftIdentity() =>
            Option.Some(4).Bind(HalfIfEven).Should().Be(HalfIfEven(4));

        [Fact]
        public void RightIdentity() =>
            Option.Some(4).Bind(Option.Some).Should().Be(Option.Some(4));
    }

    public class Where
    {
        [Fact]
        public void KeepsValue_WhenPredicateHolds() =>
            Option.Some(7).Where(x => x > 5).Should().Be(Option.Some(7));

        [Fact]
        public void FiltersOut_WhenPredicateFails() =>
            Option.Some(3).Where(x => x > 5).IsNone.Should().BeTrue();

        [Fact]
        public void KeepsNone() =>
            NoInt.Where(_ => true).IsNone.Should().BeTrue();
    }

    public class GetValueOrDefault
    {
        [Fact]
        public void ReturnsValue_WhenPresent() =>
            Option.Some(1).GetValueOrDefault(0).Should().Be(1);

        [Fact]
        public void ReturnsFallback_WhenAbsent() =>
            NoInt.GetValueOrDefault(99).Should().Be(99);
    }

    public class ToResult
    {
        [Fact]
        public void PresentBecomesOk() =>
            Option.Some(1).ToResult(() => "missing").Should().Be(Result.Ok<int, string>(1));

        [Fact]
        public void AbsentBecomesError() =>
            NoInt.ToResult(() => "missing").Should().Be(Result.Error<int, string>("missing"));

        [Fact]
        public void ErrorFactory_IsNotCalled_WhenPresent()
        {
            var called = false;

            Option.Some(1).ToResult(() => { called = true; return "missing"; });

            called.Should().BeFalse();
        }
    }

    public class Select
    {
        [Fact]
        public void BehavesLikeMap()
        {
            Option.Some(2).Select(x => x + 1).Should().Be(Option.Some(3));
            NoInt.Select(x => x + 1).IsNone.Should().BeTrue();
        }
    }

    public class SelectMany
    {
        [Fact]
        public void CombinesPresentValues() =>
            Option.Some(1)
                .SelectMany(_ => Option.Some(2), (a, b) => a + b)
                .Should().Be(Option.Some(3));

        [Fact]
        public void IsNone_WhenSourceIsNone() =>
            NoInt.SelectMany(_ => Option.Some(2), (a, b) => a + b).IsNone.Should().BeTrue();

        [Fact]
        public void IsNone_WhenNextIsNone() =>
            Option.Some(1).SelectMany(_ => NoInt, (a, b) => a + b).IsNone.Should().BeTrue();
    }

    public class QuerySyntax
    {
        [Fact]
        public void Select_MapsValue()
        {
            var result = from x in Option.Some(2) select x + 1;

            result.Should().Be(Option.Some(3));
        }

        [Fact]
        public void From_CombinesPresentValues()
        {
            var sum = from a in Option.Some(1)
                      from b in Option.Some(2)
                      select a + b;

            sum.Should().Be(Option.Some(3));
        }

        [Fact]
        public void From_IsNone_WhenAnyIsNone()
        {
            var sum = from a in Option.Some(1)
                      from b in NoInt
                      select a + b;

            sum.IsNone.Should().BeTrue();
        }

        [Fact]
        public void Where_Filters()
        {
            var result = from a in Option.Some(1)
                         where a > 1
                         select a;

            result.IsNone.Should().BeTrue();
        }

        [Fact]
        public void EqualsMethodSyntax()
        {
            var query = from a in Option.Some(1)
                        from b in Option.Some(2)
                        select a + b;

            query.Should().Be(Option.Some(1).Bind(a => Option.Some(2).Map(b => a + b)));
        }
    }

    public class Zip
    {
        [Fact]
        public void CombinesPresentValues() =>
            Option.Some(1).Zip(Option.Some(2), (a, b) => a + b).Should().Be(Option.Some(3));

        [Fact]
        public void IsNone_WhenEitherIsNone()
        {
            Option.Some(1).Zip(NoInt, (a, b) => a + b).IsNone.Should().BeTrue();
            NoInt.Zip(Option.Some(2), (a, b) => a + b).IsNone.Should().BeTrue();
        }
    }

    public class Apply
    {
        [Fact]
        public void AppliesPresentFunction_ToPresentValue() =>
            Option.Some<Func<int, int>>(x => x + 1).Apply(Option.Some(1)).Should().Be(Option.Some(2));

        [Fact]
        public void IsNone_WhenFunctionIsNone()
        {
            Option<Func<int, int>> none = Option.None;

            none.Apply(Option.Some(1)).IsNone.Should().BeTrue();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Identity(bool present)
        {
            var value = present ? Option.Some(1) : NoInt;

            Option.Some<Func<int, int>>(x => x).Apply(value).Should().Be(value);
        }
    }

    public class ToValidation
    {
        [Fact]
        public void SomeBecomesValid() =>
            Option.Some(1).ToValidation(() => "missing").Should().Be(Validation.Valid<int, string>(1));

        [Fact]
        public void NoneBecomesInvalid() =>
            NoInt.ToValidation(() => "missing").Should().Be(Validation.Invalid<int, string>("missing"));
    }
}
