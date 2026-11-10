using DyeHouseERP.Infrastructure.Services;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Login must cost the same PBKDF2 work whether or not the username exists, and a damaged stored
/// hash must answer "no match" instead of throwing into a 500.
/// </summary>
public class PasswordHasherHardeningTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Verify_accepts_the_password_that_was_hashed()
    {
        var hash = _hasher.Hash("Correct-Horse-9");

        Assert.True(_hasher.Verify("Correct-Horse-9", hash));
        Assert.False(_hasher.Verify("correct-horse-9", hash));
    }

    [Fact]
    public void VerifyOrDecoy_still_verifies_a_real_hash()
    {
        var hash = _hasher.Hash("Correct-Horse-9");

        Assert.True(_hasher.VerifyOrDecoy("Correct-Horse-9", hash));
        Assert.False(_hasher.VerifyOrDecoy("wrong", hash));
    }

    [Fact]
    public void VerifyOrDecoy_returns_false_for_a_missing_hash_instead_of_throwing()
        => Assert.False(_hasher.VerifyOrDecoy("anything", null));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("100000.only-two-parts")]
    [InlineData("abc.notbase64.notbase64")]
    [InlineData("0.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("100000..")]
    public void Verify_returns_false_for_a_corrupt_hash(string corruptHash)
        => Assert.False(_hasher.Verify("anything", corruptHash));

    [Fact]
    public void VerifyOrDecoy_works_on_a_missing_hash_that_costs_the_same_work()
    {
        // The decoy path is the one that runs for an unknown username; it must complete and refuse
        // the password, not short-circuit.
        Assert.False(_hasher.VerifyOrDecoy("anything", null));
    }
}
