using SimInventory.Api.Security;
using Xunit;

namespace SimInventory.Api.Tests;

public sealed class EmailOtpHelperTests
{
    [Fact]
    public void CreateCode_ReturnsSixDigits()
    {
        var code = EmailOtpHelper.CreateCode();
        Assert.Equal(6, code.Length);
        Assert.All(code, character => Assert.InRange(character, '0', '9'));
    }

    [Fact]
    public void HashAndVerify_AcceptsOnlyMatchingCodeAndUser()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        const string code = "042731";
        const string key = "test-only-email-otp-signing-key-32-bytes-minimum";
        var hash = EmailOtpHelper.Hash(userId, code, key);

        Assert.True(EmailOtpHelper.Verify(userId, code, hash, key));
        Assert.False(EmailOtpHelper.Verify(userId, "042732", hash, key));
        Assert.False(EmailOtpHelper.Verify(otherUserId, code, hash, key));
        Assert.False(EmailOtpHelper.Verify(userId, code, hash, "different-test-signing-key-32-bytes"));
    }
}
