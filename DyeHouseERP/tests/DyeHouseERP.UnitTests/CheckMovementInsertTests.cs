using DyeHouseERP.Application.Checks.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// H-2c regression cover: every check transition appends a movement to a check that ALREADY exists, and that
/// movement must be INSERTed. Before the fix EF change detection took the new movement (key generated
/// client-side) for an existing row and saved an UPDATE against a row that was never inserted, so
/// SaveChangesAsync threw DbUpdateConcurrencyException - HTTP 409, whole transition lost. Every lifecycle path
/// that used to be broken is exercised here through its real command handler and a real DbContext.
/// </summary>
public class CheckMovementInsertTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public CheckMovementInsertTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"check-movement-insert-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static async Task<(Guid CustomerId, Guid SupplierId, Guid AccountId)> SeedPartiesAsync(ApplicationDbContext db)
    {
        var customer = new Customer("C-1", "Customer", "tester");
        var supplier = new Supplier("SUP-1", "Supplier", "tester");
        var account = new TreasuryAccount("BANK-1", "Main Bank", TreasuryAccountKind.Bank, "tester");
        db.AddRange(customer, supplier, account);
        await db.SaveChangesAsync();
        return (customer.Id, supplier.Id, account.Id);
    }

    /// <summary>Registers a customer check through the real handler, so it is genuinely persisted.</summary>
    private static async Task<(Guid CheckId, Guid CustomerId, Guid SupplierId, Guid AccountId)> RegisterCustomerCheckAsync(
        ApplicationDbContext db, bool confirmInHandNow = true)
    {
        var (customerId, supplierId, accountId) = await SeedPartiesAsync(db);

        var dto = await new RegisterCustomerCheckCommandHandler(db, new StubCurrentUser("tester"))
            .Handle(new RegisterCustomerCheckCommand(
                "CHK-1", "Bank", 1000m, DateTime.UtcNow, DateTime.UtcNow.AddDays(30), "Issuer", customerId,
                ConfirmInHandNow: confirmInHandNow), CancellationToken.None);

        return (dto.Id, customerId, supplierId, accountId);
    }

    private static async Task<(Guid CheckId, Guid SupplierId, Guid AccountId)> RegisterSupplierCheckOnAccountAsync(
        ApplicationDbContext db)
    {
        var (_, supplierId, accountId) = await SeedPartiesAsync(db);

        var dto = await new RegisterSupplierCheckCommandHandler(db, new StubCurrentUser("tester"))
            .Handle(new RegisterSupplierCheckCommand(
                "CHK-OUT-1", "Bank", 1000m, DateTime.UtcNow, DateTime.UtcNow.AddDays(30), "Issuer", supplierId,
                TreasuryAccountId: accountId, HandOverNow: true), CancellationToken.None);

        return (dto.Id, supplierId, accountId);
    }

    private static async Task<List<CheckMovement>> MovementsAsync(ApplicationDbContext db, Guid checkId)
        => await db.CheckMovements.Where(m => m.CheckId == checkId).ToListAsync();

    [Fact]
    public async Task ConfirmingReceiptOfAPersistedCheck_InsertsTheMovement()
    {
        using var db = NewDb();
        var (checkId, _, _, _) = await RegisterCustomerCheckAsync(db, confirmInHandNow: false);
        (await MovementsAsync(db, checkId)).Should().BeEmpty("registering without confirming appends no movement");

        var dto = await new ConfirmCheckReceiptCommandHandler(db, new StubCurrentUser("tester"))
            .Handle(new ConfirmCheckReceiptCommand(checkId), CancellationToken.None);

        var movements = await MovementsAsync(db, checkId);
        movements.Should().HaveCount(1, "the received movement must be inserted, not updated");
        movements.Single().MovementType.Should().Be(CheckMovementType.Received);
        dto.Status.Should().Be(CheckStatus.InHand);
    }

    [Fact]
    public async Task DepositingAPersistedCheck_InsertsTheMovement()
    {
        using var db = NewDb();
        var (checkId, _, _, accountId) = await RegisterCustomerCheckAsync(db);

        var dto = await new DepositCheckCommandHandler(db, new StubCurrentUser("tester"))
            .Handle(new DepositCheckCommand(checkId, accountId), CancellationToken.None);

        var movements = await MovementsAsync(db, checkId);
        movements.Should().HaveCount(2, "the register movement plus the deposit");
        movements.Select(m => m.MovementType)
            .Should().BeEquivalentTo(new[] { CheckMovementType.Received, CheckMovementType.Deposited });
        dto.Status.Should().Be(CheckStatus.Deposited);
    }

    [Fact]
    public async Task ClearingAPersistedCheck_InsertsTheClearingMovementAndPostsTheCashEffectOnce()
    {
        using var db = NewDb();
        var (checkId, _, _, accountId) = await RegisterCustomerCheckAsync(db);

        var dto = await new ClearCheckCommandHandler(db, new StubCurrentUser("tester"), new OpenPeriod())
            .Handle(new ClearCheckCommand(checkId, TreasuryAccountId: accountId), CancellationToken.None);

        var movements = await MovementsAsync(db, checkId);
        // The clear deposits the in-hand check first (the account was not recorded yet), then clears it.
        movements.Should().HaveCount(3);
        movements.Select(m => m.MovementType).Should().BeEquivalentTo(new[]
        {
            CheckMovementType.Received, CheckMovementType.Deposited, CheckMovementType.Cleared
        });
        (await db.TreasuryTransactions.CountAsync()).Should().Be(1, "clearing posts the bank movement exactly once");
        dto.Status.Should().Be(CheckStatus.Cleared);
    }

    [Fact]
    public async Task ClearingAnEndorsedSupplierCheck_InsertsTheClearingMovement()
    {
        using var db = NewDb();
        var (checkId, _, accountId) = await RegisterSupplierCheckOnAccountAsync(db);

        var dto = await new ClearCheckCommandHandler(db, new StubCurrentUser("tester"), new OpenPeriod())
            .Handle(new ClearCheckCommand(checkId, TreasuryAccountId: accountId), CancellationToken.None);

        var movements = await MovementsAsync(db, checkId);
        movements.Should().HaveCount(2, "the endorsement from registration plus the clearing");
        movements.Select(m => m.MovementType)
            .Should().BeEquivalentTo(new[] { CheckMovementType.Endorsed, CheckMovementType.Cleared });
        (await db.TreasuryTransactions.CountAsync()).Should().Be(1);
        dto.Status.Should().Be(CheckStatus.Cleared);
    }

    [Fact]
    public async Task EndorsingAPersistedCheckToASupplier_InsertsTheEndorsementMovement()
    {
        using var db = NewDb();
        var (checkId, _, supplierId, _) = await RegisterCustomerCheckAsync(db);

        var dto = await new EndorseCheckToSupplierCommandHandler(db, new StubCurrentUser("tester"))
            .Handle(new EndorseCheckToSupplierCommand(checkId, supplierId, DateTime.UtcNow), CancellationToken.None);

        var movements = await MovementsAsync(db, checkId);
        movements.Should().HaveCount(2);
        movements.Select(m => m.MovementType)
            .Should().BeEquivalentTo(new[] { CheckMovementType.Received, CheckMovementType.Endorsed });
        dto.Status.Should().Be(CheckStatus.Endorsed);
    }

    [Fact]
    public async Task BouncingADepositedCheck_InsertsTheBounceMovement()
    {
        using var db = NewDb();
        var (checkId, _, _, accountId) = await RegisterCustomerCheckAsync(db);
        await new DepositCheckCommandHandler(db, new StubCurrentUser("tester"))
            .Handle(new DepositCheckCommand(checkId, accountId), CancellationToken.None);

        await new BounceCheckCommandHandler(db, new StubCurrentUser("tester"))
            .Handle(new BounceCheckCommand(checkId, "Insufficient funds"), CancellationToken.None);

        var movements = await MovementsAsync(db, checkId);
        movements.Should().HaveCount(3);
        movements.Select(m => m.MovementType)
            .Should().BeEquivalentTo(new[]
            {
                CheckMovementType.Received, CheckMovementType.Deposited, CheckMovementType.Bounced
            });
    }

    [Fact]
    public async Task ReturningABouncedCheckToTheCompany_InsertsTheReturnMovement()
    {
        using var db = NewDb();
        var (checkId, _, _, accountId) = await RegisterCustomerCheckAsync(db);
        await new DepositCheckCommandHandler(db, new StubCurrentUser("tester"))
            .Handle(new DepositCheckCommand(checkId, accountId), CancellationToken.None);
        await new BounceCheckCommandHandler(db, new StubCurrentUser("tester"))
            .Handle(new BounceCheckCommand(checkId, "Insufficient funds"), CancellationToken.None);

        var dto = await new ReturnCheckToCompanyCommandHandler(db, new StubCurrentUser("tester"))
            .Handle(new ReturnCheckToCompanyCommand(checkId), CancellationToken.None);

        var movements = await MovementsAsync(db, checkId);
        movements.Should().HaveCount(4);
        movements.Select(m => m.MovementType)
            .Should().BeEquivalentTo(new[]
            {
                CheckMovementType.Received, CheckMovementType.Deposited,
                CheckMovementType.Bounced, CheckMovementType.ReturnedToHolder
            });
        dto.Status.Should().Be(CheckStatus.InHand);
    }

    [Fact]
    public async Task CancellingAPersistedCheck_InsertsTheCancellationMovement()
    {
        using var db = NewDb();
        var (checkId, _, _, _) = await RegisterCustomerCheckAsync(db);

        var dto = await new CancelCheckCommandHandler(db, new StubCurrentUser("tester"))
            .Handle(new CancelCheckCommand(checkId, "Customer cancelled the deal"), CancellationToken.None);

        var movements = await MovementsAsync(db, checkId);
        movements.Should().HaveCount(2);
        movements.Select(m => m.MovementType)
            .Should().BeEquivalentTo(new[] { CheckMovementType.Received, CheckMovementType.Cancelled });
        dto.Status.Should().Be(CheckStatus.Cancelled);
    }

    [Fact]
    public async Task TheFullLifecycleOfOnePersistedCheck_AppendsEveryMovementExactlyOnce()
    {
        using var db = NewDb();
        var (checkId, _, _, accountId) = await RegisterCustomerCheckAsync(db);

        await new DepositCheckCommandHandler(db, new StubCurrentUser("tester"))
            .Handle(new DepositCheckCommand(checkId, accountId), CancellationToken.None);
        await new ClearCheckCommandHandler(db, new StubCurrentUser("tester"), new OpenPeriod())
            .Handle(new ClearCheckCommand(checkId, TreasuryAccountId: accountId), CancellationToken.None);

        var movements = await MovementsAsync(db, checkId);

        movements.Should().HaveCount(3, "one movement per hand-over, and not a single one lost or duplicated");
        movements.Select(m => m.MovementType).Should().OnlyHaveUniqueItems();
        movements.Select(m => m.MovementType)
            .Should().BeEquivalentTo(new[]
            {
                CheckMovementType.Received, CheckMovementType.Deposited, CheckMovementType.Cleared
            });
        (await db.CheckMovements.CountAsync()).Should().Be(3, "no duplicate movement rows anywhere in the register");
    }
}
