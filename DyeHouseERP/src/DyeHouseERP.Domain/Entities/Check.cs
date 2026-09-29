using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Check / cheque register (spec sections 37-40).
///
/// The critical rule from spec section 39: a check is ONE physical financial
/// instrument for its whole life. Receiving it from a customer and later
/// endorsing it to a supplier does NOT create cash and does NOT create a new
/// instrument - it appends a movement and changes who holds it. Every
/// transition (receipt, endorsement, deposit, clearing, bouncing, cancellation,
/// return) is a CheckMovement row carrying from/to holder, date, user and
/// reason, so the instrument stays traceable from original receipt to final
/// status.
/// </summary>
public class Check : AuditableEntity
{
    public string CheckNumber { get; private set; } = string.Empty;
    public CheckDirection Direction { get; private set; }
    public CheckStatus Status { get; private set; } = CheckStatus.Received;

    public string BankName { get; private set; } = string.Empty;
    public string? BranchName { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "EGP";

    public DateTime IssueDate { get; private set; }
    public DateTime DueDate { get; private set; }

    /// <summary>Drawer - the party who signed the instrument (spec section 37 "Issuer").</summary>
    public string Issuer { get; private set; } = string.Empty;

    public string OriginalHolder { get; private set; } = string.Empty;
    public string CurrentHolder { get; private set; } = string.Empty;
    public CheckHolderType CurrentHolderType { get; private set; }

    /// <summary>Set for an incoming customer check (spec section 37).</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>Set for an outgoing supplier check, and later re-pointed when an incoming check is endorsed onward to that supplier.</summary>
    public Guid? SupplierId { get; private set; }

    /// <summary>Bank/cash account the check was deposited into, once deposited.</summary>
    public Guid? TreasuryAccountId { get; private set; }

    /// <summary>
    /// R3: optimistic concurrency token, maintained by SQL Server. Only the
    /// Clear transition writes a treasury row, so two concurrent clears of the
    /// same check must not both post money.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public string? CustomerReference { get; private set; }
    public string? Notes { get; private set; }

    public DateTime? ReceivedAtUtc { get; private set; }
    public DateTime? DepositedAtUtc { get; private set; }
    public DateTime? ClearedAtUtc { get; private set; }
    public DateTime? BouncedAtUtc { get; private set; }
    public string? BounceReason { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

    private readonly List<CheckMovement> _movements = new();
    public IReadOnlyCollection<CheckMovement> Movements => _movements.AsReadOnly();

    private Check() { } // EF Core

    public Check(
        string checkNumber, CheckDirection direction, string bankName, decimal amount,
        DateTime issueDate, DateTime dueDate, string issuer, string originalHolder,
        CheckHolderType originalHolderType, string createdBy,
        string? branchName = null, string currency = "EGP", string? customerReference = null,
        string? notes = null, Guid? customerId = null, Guid? supplierId = null)
    {
        SetCheckNumber(checkNumber);
        SetBank(bankName, branchName);
        SetAmount(amount);
        SetDates(issueDate, dueDate);

        if (string.IsNullOrWhiteSpace(issuer))
            throw new ArgumentException("Check issuer is required.", nameof(issuer));
        if (string.IsNullOrWhiteSpace(originalHolder))
            throw new ArgumentException("The original holder of the check is required.", nameof(originalHolder));
        if (customerId.HasValue && supplierId.HasValue)
            throw new DomainException("A check belongs to a customer or a supplier, never both.");

        Direction = direction;
        Issuer = issuer.Trim();
        OriginalHolder = originalHolder.Trim();
        CurrentHolder = originalHolder.Trim();
        CurrentHolderType = originalHolderType;
        Currency = string.IsNullOrWhiteSpace(currency) ? "EGP" : currency.Trim().ToUpperInvariant();
        CustomerReference = customerReference;
        Notes = notes;
        CustomerId = customerId;
        SupplierId = supplierId;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;

        // Outgoing checks are written/handed out by the company, so they start
        // in hand and become "Endorsed" the moment they reach the supplier.
        Status = direction == CheckDirection.SupplierCheck ? CheckStatus.InHand : CheckStatus.Received;
    }

    public bool IsFinal => Status is CheckStatus.Cleared or CheckStatus.Cancelled;

    public void SetCheckNumber(string checkNumber)
    {
        if (string.IsNullOrWhiteSpace(checkNumber))
            throw new ArgumentException("Check number is required.", nameof(checkNumber));
        CheckNumber = checkNumber.Trim();
    }

    public void SetBank(string bankName, string? branchName)
    {
        if (string.IsNullOrWhiteSpace(bankName))
            throw new ArgumentException("Bank name is required.", nameof(bankName));
        BankName = bankName.Trim();
        BranchName = string.IsNullOrWhiteSpace(branchName) ? null : branchName.Trim();
    }

    public void SetAmount(decimal amount)
    {
        if (amount <= 0) throw new DomainException("A check amount must be greater than zero.");
        Amount = amount;
    }

    public void SetDates(DateTime issueDate, DateTime dueDate)
    {
        if (dueDate.Date < issueDate.Date)
            throw new DomainException("The due date of a check cannot be before its issue date.");
        IssueDate = issueDate;
        DueDate = dueDate;
    }

    public void SetReference(string? customerReference, string? notes)
    {
        CustomerReference = customerReference;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    // ------------------------------------------------------------ transitions

    /// <summary>Confirms an incoming customer check is physically in the company's hands (spec section 38 "Received" -&gt; "In Hand").</summary>
    public void ConfirmReceipt(DateTime movementDate, string companyHolderName, string by, string? notes = null)
    {
        EnsureNotFinal("receive");
        if (Direction != CheckDirection.CustomerCheck)
            throw new DomainException("Only an incoming customer check is confirmed as received.");
        if (Status is not (CheckStatus.Received or CheckStatus.InHand))
            throw new DomainException($"A check in status '{Status}' cannot be confirmed as received.");

        AddMovement(CheckMovementType.Received, CurrentHolder, companyHolderName, CheckHolderType.Company,
            movementDate, by, reason: null, supplierId: null, treasuryAccountId: null, notes);

        CurrentHolder = companyHolderName;
        CurrentHolderType = CheckHolderType.Company;
        Status = CheckStatus.InHand;
        ReceivedAtUtc ??= DateTime.UtcNow;
        Touch(by);
    }

    /// <summary>
    /// Hands the SAME instrument to a supplier (spec section 39: Customer -&gt; Company -&gt; Supplier). No new check,
    /// no cash: the holder changes and the movement history grows.
    /// </summary>
    public void EndorseToSupplier(Guid supplierId, string supplierName, DateTime movementDate, string by, string? reason)
    {
        EnsureNotFinal("endorse");
        if (Status is CheckStatus.Deposited)
            throw new DomainException("A check that is already deposited cannot be endorsed to a supplier.");

        AddMovement(CheckMovementType.Endorsed, CurrentHolder, supplierName, CheckHolderType.Supplier,
            movementDate, by, reason, supplierId, treasuryAccountId: null, notes: null);

        CurrentHolder = supplierName;
        CurrentHolderType = CheckHolderType.Supplier;
        SupplierId = supplierId;
        Status = CheckStatus.Endorsed;
        Touch(by);
    }

    public void Deposit(Guid treasuryAccountId, string bankAccountName, DateTime movementDate, string by, string? notes = null)
    {
        EnsureNotFinal("deposit");
        if (Status is not (CheckStatus.Received or CheckStatus.InHand))
            throw new DomainException($"A check in status '{Status}' cannot be deposited - it must be in hand first.");
        if (treasuryAccountId == Guid.Empty)
            throw new DomainException("A bank/treasury account is required to deposit a check.");

        AddMovement(CheckMovementType.Deposited, CurrentHolder, bankAccountName, CheckHolderType.Bank,
            movementDate, by, reason: null, supplierId: null, treasuryAccountId, notes);

        CurrentHolder = bankAccountName;
        CurrentHolderType = CheckHolderType.Bank;
        TreasuryAccountId = treasuryAccountId;
        Status = CheckStatus.Deposited;
        DepositedAtUtc = DateTime.UtcNow;
        Touch(by);
    }

    /// <summary>
    /// Records which bank/cash account the instrument is drawn on or was deposited into,
    /// without changing who holds it. Used for outgoing supplier checks, where the account
    /// matters for the cash effect but the check has not been "deposited" by the company.
    /// </summary>
    public void SetDrawnOnAccount(Guid treasuryAccountId)
    {
        if (treasuryAccountId == Guid.Empty)
            throw new DomainException("A bank/treasury account is required.");
        TreasuryAccountId = treasuryAccountId;
    }

    /// <summary>
    /// Marks the instrument as cleared at the bank. Allowed from Deposited (our own deposit) or from
    /// Endorsed (an outgoing supplier check presented by the supplier) - both are real clearing paths.
    /// </summary>
    public void Clear(DateTime movementDate, string by, string? notes = null)
    {
        EnsureNotFinal("clear");
        if (Status is not (CheckStatus.Deposited or CheckStatus.Endorsed))
            throw new DomainException("Only a deposited or endorsed check can be marked as cleared.");

        AddMovement(CheckMovementType.Cleared, CurrentHolder, CurrentHolder, CheckHolderType.Bank,
            movementDate, by, reason: null, supplierId: null, TreasuryAccountId, notes);

        Status = CheckStatus.Cleared;
        ClearedAtUtc = DateTime.UtcNow;
        Touch(by);
        Lock();
    }

    public void Bounce(DateTime movementDate, string by, string reason)
    {
        EnsureNotFinal("bounce");
        if (Status != CheckStatus.Deposited)
            throw new DomainException("Only a deposited check can be returned/bounced by the bank.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A reason is required when a check is returned/bounced.");

        // A bounced check is still with the bank until it is physically returned
        // (ReturnToCompany records that separately) - the holder is never rewritten silently.
        AddMovement(CheckMovementType.Bounced, CurrentHolder, CurrentHolder, CheckHolderType.Bank,
            movementDate, by, reason, supplierId: null, treasuryAccountId: null, notes: null);

        Status = CheckStatus.Bounced;
        BouncedAtUtc = DateTime.UtcNow;
        BounceReason = reason.Trim();
        Touch(by);
    }

    /// <summary>Returns the instrument to the company's hands (e.g. a bounced or endorsed check coming back).</summary>
    public void ReturnToCompany(DateTime movementDate, string companyHolderName, string by, string? reason)
    {
        EnsureNotFinal("return");
        if (Status is CheckStatus.Received or CheckStatus.InHand)
            throw new DomainException("This check is already in the company's hands.");

        AddMovement(CheckMovementType.ReturnedToHolder, CurrentHolder, companyHolderName, CheckHolderType.Company,
            movementDate, by, reason, supplierId: SupplierId, treasuryAccountId: null, notes: null);

        CurrentHolder = companyHolderName;
        CurrentHolderType = CheckHolderType.Company;
        Status = CheckStatus.InHand;
        Touch(by);
    }

    public void Cancel(DateTime movementDate, string by, string reason)
    {
        if (Status == CheckStatus.Cancelled) return;
        if (Status == CheckStatus.Cleared)
            throw new DomainException("A cleared check can no longer be cancelled.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A cancellation reason is required.");

        AddMovement(CheckMovementType.Cancelled, CurrentHolder, CurrentHolder, CurrentHolderType,
            movementDate, by, reason, supplierId: null, treasuryAccountId: null, notes: null);

        Status = CheckStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = reason.Trim();
        Touch(by);
        Lock();
    }

    private CheckMovement AddMovement(
        CheckMovementType movementType, string fromHolder, string toHolder, CheckHolderType toHolderType,
        DateTime movementDate, string by, string? reason, Guid? supplierId, Guid? treasuryAccountId, string? notes)
    {
        var movement = new CheckMovement(
            Id, movementType, fromHolder, toHolder, toHolderType, movementDate, by,
            reason, supplierId, treasuryAccountId, notes);
        _movements.Add(movement);
        return movement;
    }

    private void EnsureNotFinal(string action)
    {
        if (IsFinal)
            throw new DocumentLockedException("Check", $"{BankName}/{CheckNumber}");
        if (IsLocked)
            throw new DomainException($"This check is locked and cannot be {action}ed directly.");
    }

    private void Touch(string modifiedBy)
    {
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// One physical movement of one check: who held it, who holds it now, when, why
/// and by whom (spec sections 38-39). Append-only - never edited, never deleted.
/// </summary>
public class CheckMovement : BaseEntity
{
    public Guid CheckId { get; private set; }
    public CheckMovementType MovementType { get; private set; }
    public string FromHolder { get; private set; } = string.Empty;
    public string ToHolder { get; private set; } = string.Empty;
    public CheckHolderType ToHolderType { get; private set; }
    public DateTime MovementDate { get; private set; }
    public string? Reason { get; private set; }
    public Guid? SupplierId { get; private set; }
    public Guid? TreasuryAccountId { get; private set; }
    public string? Notes { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    private CheckMovement() { } // EF Core

    internal CheckMovement(
        Guid checkId, CheckMovementType movementType, string fromHolder, string toHolder,
        CheckHolderType toHolderType, DateTime movementDate, string createdBy,
        string? reason, Guid? supplierId, Guid? treasuryAccountId, string? notes)
    {
        CheckId = checkId;
        MovementType = movementType;
        FromHolder = fromHolder;
        ToHolder = toHolder;
        ToHolderType = toHolderType;
        MovementDate = movementDate;
        CreatedBy = createdBy;
        Reason = reason;
        SupplierId = supplierId;
        TreasuryAccountId = treasuryAccountId;
        Notes = notes;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
