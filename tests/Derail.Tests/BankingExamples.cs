using AwesomeAssertions;
using Derail;
using Xunit;

namespace Derail.Tests;

/// <summary>
/// Executable examples using a small banking domain: looking up accounts (Option),
/// transferring money (Result), opening accounts (Validation), required payment batches
/// (NonEmpty), explicit dependencies (Reader), audit events (Writer) and async pipelines.
/// </summary>
public class BankingExamples
{
    private sealed record AccountNumber(string Value)
    {
        public override string ToString() => Value;
    }

    private sealed record Account(AccountNumber Number, string Owner, decimal Balance, bool Frozen = false, string? Nickname = null);

    private sealed record Receipt(AccountNumber From, AccountNumber To, decimal Amount, decimal FromBalance, decimal ToBalance);

    private abstract record TransferError
    {
        public sealed record MalformedAccountNumber(string Input) : TransferError;
        public sealed record AccountNotFound(AccountNumber Number) : TransferError;
        public sealed record AccountFrozen(AccountNumber Number) : TransferError;
        public sealed record NonPositiveAmount(decimal Amount) : TransferError;
        public sealed record SameAccount(AccountNumber Number) : TransferError;
        public sealed record InsufficientFunds(AccountNumber Number, decimal Balance, decimal Requested) : TransferError;
    }

    private static readonly Account Savings = new(new("12345678901"), "Kari", 1_000m, Nickname: "Rainy day");
    private static readonly Account Checking = new(new("10987654321"), "Kari", 50m);
    private static readonly Account Seized = new(new("11111111111"), "Ola", 5_000m, Frozen: true);

    private static readonly Dictionary<AccountNumber, Account> Ledger =
        new[] { Savings, Checking, Seized }.ToDictionary(a => a.Number);

    // --- Building blocks -------------------------------------------------------------

    private static Option<Account> FindAccount(AccountNumber number) =>
        FindAccount(number, Ledger);

    private static Option<Account> FindAccount(AccountNumber number, IReadOnlyDictionary<AccountNumber, Account> ledger) =>
        ledger.TryGetValue(number, out var account) ? Option.Some(account) : Option.None;

    private static Result<AccountNumber, TransferError> ParseAccountNumber(string input)
    {
        var digits = input.Replace(".", "").Replace(" ", "");
        return digits.Length == 11 && digits.All(char.IsAsciiDigit)
            ? Result.Ok<AccountNumber, TransferError>(new AccountNumber(digits))
            : Result.Error<AccountNumber, TransferError>(new TransferError.MalformedAccountNumber(input));
    }

    private static Result<Account, TransferError> GetAccount(AccountNumber number) =>
        FindAccount(number).ToResult(TransferError () => new TransferError.AccountNotFound(number));

    private static Result<Account, TransferError> EnsureActive(Account account) =>
        account.Frozen
            ? Result.Error<Account, TransferError>(new TransferError.AccountFrozen(account.Number))
            : Result.Ok<Account, TransferError>(account);

    private static Result<decimal, TransferError> EnsurePositive(decimal amount) =>
        amount > 0
            ? Result.Ok<decimal, TransferError>(amount)
            : Result.Error<decimal, TransferError>(new TransferError.NonPositiveAmount(amount));

    private static Result<Account, TransferError> Withdraw(Account account, decimal amount) =>
        account.Balance >= amount
            ? Result.Ok<Account, TransferError>(account with { Balance = account.Balance - amount })
            : Result.Error<Account, TransferError>(new TransferError.InsufficientFunds(account.Number, account.Balance, amount));

    private static Result<Account, TransferError> EnsureDifferent(Account source, Account target) =>
        source.Number == target.Number
            ? Result.Error<Account, TransferError>(new TransferError.SameAccount(source.Number))
            : Result.Ok<Account, TransferError>(target);

    private static Account Deposit(Account account, decimal amount) => account with { Balance = account.Balance + amount };

    private static Result<Account, TransferError> OpenAccount(string input) =>
        OpenAccount(input, Ledger);

    private static Result<Account, TransferError> OpenAccount(string input, IReadOnlyDictionary<AccountNumber, Account> ledger) =>
        ParseAccountNumber(input)
            .Bind(number => FindAccount(number, ledger).ToResult(TransferError () => new TransferError.AccountNotFound(number)))
            .Bind(EnsureActive);

    // --- The transfer pipeline -------------------------------------------------------

    /// <summary>Query syntax keeps every intermediate value in scope, which a transfer needs.</summary>
    private static Result<Receipt, TransferError> Transfer(string fromInput, string toInput, decimal amount) =>
        Transfer(fromInput, toInput, amount, Ledger);

    private static Result<Receipt, TransferError> Transfer(
        string fromInput, string toInput, decimal amount, IReadOnlyDictionary<AccountNumber, Account> ledger) =>
        from value in EnsurePositive(amount)
        from source in OpenAccount(fromInput, ledger)
        from target in OpenAccount(toInput, ledger)
        from _ in EnsureDifferent(source, target)
        from debited in Withdraw(source, value)
        let credited = Deposit(target, value)
        select new Receipt(source.Number, target.Number, value, debited.Balance, credited.Balance);

    private static string Describe(Result<Receipt, TransferError> result) => result switch
    {
        Ok<Receipt>(var r) => $"Moved {r.Amount:0.00} from {r.From} to {r.To}",
        Error<TransferError>(TransferError.MalformedAccountNumber(var input)) => $"'{input}' is not an account number",
        Error<TransferError>(TransferError.AccountNotFound(var number)) => $"No account {number}",
        Error<TransferError>(TransferError.AccountFrozen(var number)) => $"Account {number} is frozen",
        Error<TransferError>(TransferError.NonPositiveAmount(var amount)) => $"Amount must be positive, was {amount:0.00}",
        Error<TransferError>(TransferError.SameAccount(var number)) => $"Cannot transfer from {number} to itself",
        Error<TransferError>(TransferError.InsufficientFunds(var number, var balance, var requested)) =>
            $"Account {number} has {balance:0.00}, needs {requested:0.00}",
        Error<TransferError> => "Unknown error",
    };

    // --- Option ----------------------------------------------------------------------

    [Fact]
    public void Option_DisplayNameFallsBackToAccountNumber()
    {
        static string DisplayName(Account account) =>
            Option.FromNullable(account.Nickname).GetValueOrDefault(account.Number.Value);

        DisplayName(Savings).Should().Be("Rainy day");
        DisplayName(Checking).Should().Be("10987654321");
    }

    [Fact]
    public void Option_LookupThenMap()
    {
        var balance = FindAccount(Savings.Number).Map(a => a.Balance);
        var missing = FindAccount(new AccountNumber("00000000000")).Map(a => a.Balance);

        balance.Should().Be(Option.Some(1_000m));
        missing.IsNone.Should().BeTrue();
    }

    [Fact]
    public void Option_WhereKeepsOnlyRichAccounts()
    {
        Option<Account> Premium(AccountNumber n) => FindAccount(n).Where(a => a.Balance >= 500m);

        Premium(Savings.Number).IsSome.Should().BeTrue();
        Premium(Checking.Number).IsNone.Should().BeTrue();
    }

    [Fact]
    public void Option_ZipCombinedBalance()
    {
        var total = FindAccount(Savings.Number).Zip(FindAccount(Checking.Number), (a, b) => a.Balance + b.Balance);

        total.Should().Be(Option.Some(1_050m));
    }

    // --- Result ----------------------------------------------------------------------

    [Fact]
    public void Transfer_Succeeds()
    {
        var receipt = Transfer("1234.56.78901", "1098.76.54321", 200m);

        receipt.Should().Be(Result.Ok<Receipt, TransferError>(
            new Receipt(Savings.Number, Checking.Number, 200m, FromBalance: 800m, ToBalance: 250m)));
    }

    [Theory]
    [InlineData("1234.56.78901", "1098.76.54321", 200, "Moved 200.00 from 12345678901 to 10987654321")]
    [InlineData("1234", "1098.76.54321", 200, "'1234' is not an account number")]
    [InlineData("1234.56.78901", "0000.00.00000", 200, "No account 00000000000")]
    [InlineData("1111.11.11111", "1098.76.54321", 200, "Account 11111111111 is frozen")]
    [InlineData("1234.56.78901", "1098.76.54321", -5, "Amount must be positive, was -5.00")]
    [InlineData("1234.56.78901", "1234.56.78901", 10, "Cannot transfer from 12345678901 to itself")]
    [InlineData("1098.76.54321", "1234.56.78901", 75, "Account 10987654321 has 50.00, needs 75.00")]
    public void Transfer_ReportsEachFailure(string from, string to, decimal amount, string expected) =>
        Describe(Transfer(from, to, amount)).Should().Be(expected);

    [Fact]
    public void Transfer_StopsAtFirstError()
    {
        // Both accounts are bad, but only the first problem is reported.
        var result = Transfer("nope", "also nope", 10m);

        result.Should().Be(Result.Error<Receipt, TransferError>(new TransferError.MalformedAccountNumber("nope")));
    }

    [Fact]
    public void MethodSyntax_ForLinearPipelines()
    {
        var balance = ParseAccountNumber("1234.56.78901")
            .Bind(GetAccount)
            .Bind(EnsureActive)
            .Bind(a => Withdraw(a, 100m))
            .Map(a => a.Balance);

        balance.Should().Be(Result.Ok<decimal, TransferError>(900m));
    }

    [Theory]
    [InlineData("1098.76.54321", 10, 200)]
    [InlineData("1234", 100, 400)]
    [InlineData("0000.00.00000", 100, 404)]
    [InlineData("1111.11.11111", 100, 423)]
    [InlineData("1098.76.54321", 100, 409)]
    public void MapError_TranslatesToHttpStatusAtTheBoundary(string from, decimal amount, int expected)
    {
        var status = Transfer(from, "1234.56.78901", amount)
            .MapError(error => error switch
            {
                TransferError.AccountNotFound => 404,
                TransferError.AccountFrozen => 423,
                TransferError.InsufficientFunds => 409,
                _ => 400,
            })
            .Match(_ => 200, code => code);

        status.Should().Be(expected);
    }

    // --- Validation ------------------------------------------------------------------

    private sealed record AccountApplication(string Owner, AccountNumber Number, decimal InitialDeposit);

    private static Validation<string, string> ValidateOwner(string owner) =>
        string.IsNullOrWhiteSpace(owner)
            ? Validation.Invalid<string, string>("Owner is required")
            : Validation.Valid<string, string>(owner.Trim());

    private static Validation<AccountNumber, string> ValidateNumber(string input) =>
        ParseAccountNumber(input).ToValidation().MapErrors(_ => $"'{input}' is not an account number");

    private static Validation<decimal, string> ValidateDeposit(decimal amount) =>
        amount >= 100m
            ? Validation.Valid<decimal, string>(amount)
            : Validation.Invalid<decimal, string>($"Initial deposit must be at least 100.00, was {amount:0.00}");

    private static Validation<AccountApplication, string> ValidateApplication(string owner, string number, decimal deposit) =>
        ValidateOwner(owner).Zip(ValidateNumber(number), ValidateDeposit(deposit),
            (o, n, d) => new AccountApplication(o, n, d));

    [Fact]
    public void Validation_AcceptsGoodApplication() =>
        ValidateApplication(" Kari ", "1234.56.78901", 500m).Should().Be(
            Validation.Valid<AccountApplication, string>(new AccountApplication("Kari", Savings.Number, 500m)));

    [Fact]
    public void Validation_CollectsEveryError() =>
        ValidateApplication("", "12", 10m).Should().Be(Validation.Invalid<AccountApplication, string>(
            "Owner is required",
            "'12' is not an account number",
            "Initial deposit must be at least 100.00, was 10.00"));

    [Fact]
    public void Validation_SequenceChecksABatchOfPayments()
    {
        string[] batch = ["1234.56.78901", "bad", "1098.76.54321", "worse"];

        var result = Validation.Sequence(batch.Select(ValidateNumber));

        result.Should().Be(Validation.Invalid<IReadOnlyList<AccountNumber>, string>(
            "'bad' is not an account number",
            "'worse' is not an account number"));
    }

    [Fact]
    public void Validation_ThenContinueAsResult()
    {
        Result<AccountApplication, NonEmpty<string>> application = ValidateApplication("", "12", 500m).ToResult();

        var message = application
            .Map(a => $"Welcome, {a.Owner}")
            .Match(ok => ok, errors => string.Join("; ", errors));

        message.Should().Be("Owner is required; '12' is not an account number");
    }

    [Fact]
    public void Validation_RejectedApplicationHasARequiredFirstError()
    {
        var rejection = ValidateApplication("", "12", 10m);

        var summary = rejection.Match(
            _ => "Application accepted",
            errors => $"{errors.Head} ({errors.Tail.Count} more problems)");

        summary.Should().Be("Owner is required (2 more problems)");
        (rejection is Invalid<string>(var errors) && errors.Head == "Owner is required"
            && errors.Count == 3).Should().BeTrue();
    }

    // --- NonEmpty, Reader and Writer --------------------------------------------------

    private sealed record Payment(AccountNumber To, decimal Amount);
    private sealed record TransferRecorded(AccountNumber From, AccountNumber To, decimal Amount);

    private static Result<NonEmpty<Payment>, string> RequirePayments(IEnumerable<Payment> payments) =>
        NonEmpty.FromEnumerable(payments).ToResult(() => "At least one payment is required");

    private static Reader<IReadOnlyDictionary<AccountNumber, Account>, decimal> TotalBalance() =>
        Reader.Ask<IReadOnlyDictionary<AccountNumber, Account>>()
            .Map(ledger => ledger.Values.Sum(account => account.Balance));

    private static ReaderResult<IReadOnlyDictionary<AccountNumber, Account>, Writer<Receipt, TransferRecorded>, TransferError>
        AuditedTransfer(string fromInput, string toInput, decimal amount) =>
        new(ledger => Transfer(fromInput, toInput, amount, ledger)
            .Map(receipt => new Writer<Receipt, TransferRecorded>(receipt,
                [new TransferRecorded(receipt.From, receipt.To, receipt.Amount)])));

    [Fact]
    public void NonEmpty_PaymentBatchRequiresAtLeastOnePayment()
    {
        var batch = RequirePayments([
            new Payment(Checking.Number, 200m),
            new Payment(Seized.Number, 50m),
        ]);

        batch.Map(payments => payments.Map(payment => payment.Amount).Sum())
            .Should().Be(Result.Ok<decimal, string>(250m));
        RequirePayments([]).Should().Be(
            Result.Error<NonEmpty<Payment>, string>("At least one payment is required"));
    }

    [Fact]
    public void Reader_BalanceReportUsesTheSuppliedLedger()
    {
        var report = TotalBalance();

        report.Run(Ledger).Should().Be(6_050m);
        report.Run(new Dictionary<AccountNumber, Account> { [Checking.Number] = Checking })
            .Should().Be(50m);
    }

    [Fact]
    public void Writer_SuccessfulTransferReturnsAnAuditEvent()
    {
        var result = AuditedTransfer("1234.56.78901", "1098.76.54321", 200m).Run(Ledger);
        var audited = result.Match(value => value, error => throw new InvalidOperationException(error.ToString()));
        var expected = new Receipt(Savings.Number, Checking.Number, 200m, 800m, 250m);

        audited.Value.Should().Be(expected);
        audited.Events.Should().Equal(new TransferRecorded(expected.From, expected.To, expected.Amount));
        // The workflow calculates a proposal; it neither updates the ledger nor publishes events.
        Ledger[Savings.Number].Balance.Should().Be(1_000m);
        Ledger[Checking.Number].Balance.Should().Be(50m);
    }

    [Fact]
    public void ReaderResult_TransferUsesTheSuppliedBalancesAndReturnsNoWriterOnFailure()
    {
        var workflow = AuditedTransfer("1234.56.78901", "1098.76.54321", 200m);
        var ledger = new Dictionary<AccountNumber, Account>(Ledger)
        {
            [Savings.Number] = Savings with { Balance = 100m },
        };

        workflow.Run(ledger).Should().Be(Result.Error<Writer<Receipt, TransferRecorded>, TransferError>(
            new TransferError.InsufficientFunds(Savings.Number, 100m, 200m)));
        workflow.Run(Ledger).IsOk.Should().BeTrue();
    }

    // --- Async -----------------------------------------------------------------------

    private static async Task<Option<Account>> FindAccountAsync(AccountNumber number)
    {
        await Task.Yield();
        return FindAccount(number);
    }

    private static async Task<decimal> ExchangeRateAsync(string currency)
    {
        await Task.Yield();
        return currency == "EUR" ? 0.085m : 1m;
    }

    private static Task<Result<decimal, TransferError>> BalanceInAsync(string input, string currency) =>
        ParseAccountNumber(input)
            .BindAsync(n => FindAccountAsync(n).ToResult(TransferError () => new TransferError.AccountNotFound(n)))
            .Bind(EnsureActive)
            .MapAsync(async a => a.Balance * await ExchangeRateAsync(currency));

    [Fact]
    public async Task Async_PipelineReadsLikeTheSyncOne()
    {
        (await BalanceInAsync("1234.56.78901", "EUR")).Should().Be(Result.Ok<decimal, TransferError>(85m));
        (await BalanceInAsync("1111.11.11111", "EUR")).Should().Be(
            Result.Error<decimal, TransferError>(new TransferError.AccountFrozen(Seized.Number)));
    }
}
