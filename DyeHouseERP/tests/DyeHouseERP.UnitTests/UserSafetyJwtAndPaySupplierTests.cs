using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Purchases.Commands;
using DyeHouseERP.Application.Users.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// H1: DeactivateUserCommand must carry the same two safety rules as
/// UpdateUserCommand - no self-deactivation, no deactivating the last
/// active administrator.
/// </summary>
public class DeactivateUserSafetyTests
{
    private readonly DbContextOptions<Persistence.ApplicationDbContext> _options;

    public DeactivateUserSafetyTests()
    {
        // The full SQL Server model carries provider-specific column types
        // (nvarchar(max)), so these handler tests use the typeless InMemory
        // provider; the business rules under test do not depend on relational
        // constraints.
        _options = new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"users-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private Persistence.ApplicationDbContext NewDb() => new(_options);

    private static ICurrentUserService AsUser(string username, Guid userId) =>
        Mock.Of<ICurrentUserService>(u => u.UserName == username && u.UserId == userId);

    [Fact]
    public async Task User_Cannot_Deactivate_Themself()
    {
        var me = new User("boss", "hash", "Boss", "admin", "seed");
        using (var db = NewDb())
        {
            db.Users.Add(me);
            db.SaveChanges();
        }

        var handler = new DeactivateUserCommandHandler(NewDb(), AsUser("boss", me.Id));

        var act = async () => await handler.Handle(new DeactivateUserCommand(me.Id), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Message.Should().Contain("cannot deactivate your own account");

        using var verify = NewDb();
        verify.Users.First(u => u.Id == me.Id).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Last_Active_Admin_Cannot_Be_Deactivated()
    {
        var admin = new User("only-admin", "hash", "Only Admin", "admin", "seed");
        using (var db = NewDb())
        {
            db.Users.Add(admin);
            db.SaveChanges();
        }

        var handler = new DeactivateUserCommandHandler(NewDb(), AsUser("someone-else", Guid.NewGuid()));

        var act = async () => await handler.Handle(new DeactivateUserCommand(admin.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Contain("only active administrator");

        using var verify = NewDb();
        verify.Users.First(u => u.Id == admin.Id).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Admin_Can_Be_Deactivated_When_Another_Active_Admin_Remains()
    {
        var target = new User("admin-b", "hash", "Admin B", "admin", "seed");
        var other = new User("admin-a", "hash", "Admin A", "admin", "seed");
        using (var db = NewDb())
        {
            db.Users.AddRange(target, other);
            db.SaveChanges();
        }

        var handler = new DeactivateUserCommandHandler(NewDb(), AsUser("admin-a", other.Id));
        await handler.Handle(new DeactivateUserCommand(target.Id), CancellationToken.None);

        using var verify = NewDb();
        verify.Users.First(u => u.Id == target.Id).IsActive.Should().BeFalse();
        verify.Users.First(u => u.Id == other.Id).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Normal_User_Can_Be_Deactivated()
    {
        var user = new User("clerk", "hash", "Clerk", "inventory.view", "seed");
        using (var db = NewDb())
        {
            db.Users.Add(user);
            db.SaveChanges();
        }

        var handler = new DeactivateUserCommandHandler(NewDb(), AsUser("boss", Guid.NewGuid()));
        await handler.Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);

        using var verify = NewDb();
        verify.Users.First(u => u.Id == user.Id).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivated_Admin_Without_Other_Active_Admins_Is_Not_Counted_As_Protection()
    {
        // The "other" admin is inactive, so the target stays the last ACTIVE admin.
        var target = new User("admin-live", "hash", "Live Admin", "admin", "seed");
        var inactiveAdmin = new User("admin-gone", "hash", "Gone Admin", "admin", "seed");
        inactiveAdmin.Deactivate("seed");
        using (var db = NewDb())
        {
            db.Users.AddRange(target, inactiveAdmin);
            db.SaveChanges();
        }

        var handler = new DeactivateUserCommandHandler(NewDb(), AsUser("someone-else", Guid.NewGuid()));

        (await Assert.ThrowsAnyAsync<DomainException>(
                () => handler.Handle(new DeactivateUserCommand(target.Id), CancellationToken.None)))
            .Message.Should().Contain("only active administrator");
    }
}

/// <summary>
/// H2: the JWT key validator rejects blank/placeholder keys, has no fallback,
/// and accepts a real configured key. Production minimums hold for real keys.
/// </summary>
public class JwtKeyValidatorTests
{
    [Theory]
    [InlineData(null)]
    public void Null_Key_Is_Reported_As_Absent_Not_Accepted(string? key) =>
        JwtKeyValidator.Validate(key, productionMinimums: false).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_Key_Is_Rejected(string? key) =>
        Assert.Throws<InvalidOperationException>(() => JwtKeyValidator.Validate(key, productionMinimums: false));

    [Theory]
    [InlineData("CHANGE_ME_use_a_long_random_secret_in_production")]
    [InlineData("change_me_lower_case_also_rejected")]
    [InlineData("placeholder")]
    [InlineData("your-secret-key")]
    [InlineData("not-a-real-secret")]
    public void Placeholder_Keys_Are_Rejected(string key) =>
        Assert.Throws<InvalidOperationException>(() => JwtKeyValidator.Validate(key, productionMinimums: false));

    [Fact]
    public void Valid_Key_Is_Accepted_And_Trimmed_Rules_Applied()
    {
        var key = "a-reasonably-strong-development-key-0123456789";
        JwtKeyValidator.Validate(key, productionMinimums: false).Should().Be(key);
    }

    [Fact]
    public void There_Is_No_Fallback_Secret_Anywhere_In_The_Validator()
    {
        // Every rejectable input stays rejected; the validator never invents a key.
        foreach (var bad in new[] { "", " ", "CHANGE_ME", "secret" })
            Assert.Throws<InvalidOperationException>(() => JwtKeyValidator.Validate(bad, productionMinimums: false));

        JwtKeyValidator.Validate(null, productionMinimums: false).Should().BeNull();
    }

    [Fact]
    public void Short_Keys_Are_Rejected_In_Production_But_Allowed_For_Explicit_Dev_Keys()
    {
        var shortKey = "short-key-123";
        Assert.Throws<InvalidOperationException>(() => JwtKeyValidator.Validate(shortKey, productionMinimums: true));
        JwtKeyValidator.Validate(shortKey, productionMinimums: false).Should().Be(shortKey);
    }

    [Fact]
    public void JwtTokenService_Uses_The_Same_Validator()
    {
        var config = new Moq.Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        var section = new Moq.Mock<Microsoft.Extensions.Configuration.IConfigurationSection>();
        section.SetupGet(s => s["Key"]).Returns("CHANGE_ME_still_rejected");
        config.Setup(c => c.GetSection("Jwt")).Returns(section.Object);

        var service = new JwtTokenService(config.Object);
        var act = () => service.GenerateToken(Guid.NewGuid(), "u", Array.Empty<string>(), out _);
        act.Should().Throw<InvalidOperationException>().WithMessage("*placeholder*");
    }
}

/// <summary>
/// H3: a supplier payment that references an invoice must fail unless the
/// invoice belongs to the same supplier and is Posted. Nothing may be written
/// (treasury or supplier ledger) when the guard fires.
/// </summary>
public class PaySupplierOwnershipTests
{
    private readonly DbContextOptions<Persistence.ApplicationDbContext> _options;

    public PaySupplierOwnershipTests()
    {
        _options = new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"paysupplier-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private Persistence.ApplicationDbContext NewDb() => new(_options);

    private sealed class FixedNumberGenerator : IDocumentNumberGenerator
    {
        public Task<string> NextAsync(DocumentType documentType, Guid? warehouseId = null, CancellationToken cancellationToken = default)
            => Task.FromResult($"{documentType}-TEST-1");
    }

    private sealed class OpenPeriod : IPeriodCloseService
    {
        public Task EnsureOpenAsync(DateTime date, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private (Guid supplierA, Guid supplierB, Guid account, Guid invoice) Seed()
    {
        var supplierA = new Supplier("SUP-A", "Supplier A", "seed");
        var supplierB = new Supplier("SUP-B", "Supplier B", "seed");
        var account = new TreasuryAccount("BANK-1", "Main Bank", TreasuryAccountKind.Bank, "seed");
        var invoice = new SupplierInvoice("SI-1", DateTime.UtcNow, DateTime.UtcNow.AddDays(30), supplierB.Id, "seed");
        invoice.AddLine(Guid.Empty, "test line", 1m, default(MaterialUnit), 100m);
        using var db = NewDb();
        db.Suppliers.AddRange(supplierA, supplierB);
        db.TreasuryAccounts.Add(account);
        db.SupplierInvoices.Add(invoice);
        db.SaveChanges();
        return (supplierA.Id, supplierB.Id, account.Id, invoice.Id);
    }

    private static PaySupplierCommandHandler Handler(Persistence.ApplicationDbContext db) => new(
        db,
        Mock.Of<ICurrentUserService>(u => u.UserName == "payer"),
        new FixedNumberGenerator(),
        new OpenPeriod());

    private PaySupplierCommand Payment(Guid supplier, Guid account, Guid invoiceId) =>
        new(DateTime.UtcNow, supplier, account, 100m, "cash", CheckId: null, SupplierInvoiceId: invoiceId);

    [Fact]
    public async Task Matching_Supplier_With_Posted_Invoice_Is_Allowed()
    {
        var (_, supplierB, account, invoice) = Seed();
        using (var db = NewDb())
        {
            db.SupplierInvoices.Include(i => i.Lines).First(i => i.Id == invoice).Post("seed");
            db.SaveChanges();
        }

        using var db2 = NewDb();
        await Handler(db2).Handle(Payment(supplierB, account, invoice), CancellationToken.None);

        db2.TreasuryTransactions.Should().ContainSingle(t => t.Amount == 100m && t.Direction == TreasuryDirection.Out);
        db2.SupplierLedgerEntries.Should().ContainSingle(e => e.Credit == 100m);
    }

    [Fact]
    public async Task Different_Supplier_With_Posted_Invoice_Is_Rejected()
    {
        var (supplierA, _, account, invoice) = Seed();
        using (var db = NewDb())
        {
            db.SupplierInvoices.Include(i => i.Lines).First(i => i.Id == invoice).Post("seed");
            db.SaveChanges();
        }

        using var db2 = NewDb();
        var act = async () => await Handler(db2).Handle(Payment(supplierA, account, invoice), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Contain("belongs to a different supplier");

        // Nothing was posted anywhere.
        db2.TreasuryTransactions.Should().BeEmpty();
        db2.SupplierLedgerEntries.Should().BeEmpty();
        db2.SupplierPayments.Should().BeEmpty();
    }

    [Fact]
    public async Task Draft_Invoice_Is_Rejected()
    {
        var (_, supplierB, account, invoice) = Seed(); // stays Draft

        using var db = NewDb();
        var act = async () => await Handler(db).Handle(Payment(supplierB, account, invoice), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Contain("only a posted invoice can be paid");
        db.SupplierPayments.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelled_Invoice_Is_Rejected()
    {
        var (_, supplierB, account, invoice) = Seed();
        using (var db = NewDb())
        {
            var posted = db.SupplierInvoices.Include(i => i.Lines).First(i => i.Id == invoice);
            posted.Post("seed");
            posted.Cancel("seed", "wrong document");
            db.SaveChanges();
        }

        using var db2 = NewDb();
        var act = async () => await Handler(db2).Handle(Payment(supplierB, account, invoice), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*only a posted invoice can be paid*");
        db2.SupplierPayments.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_Invoice_Is_Rejected()
    {
        var (supplierB, _, account, _) = Seed();
        using var db = NewDb();
        var act = async () => await Handler(db).Handle(
            Payment(supplierB, account, Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<DyeHouseERP.Application.Common.Exceptions.NotFoundException>();
    }
}
