using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class VerificationCodeDetectorTests
{
    [Fact]
    public void Extracts_code_nearest_the_keyword_not_the_footer_number()
    {
        bool ok = VerificationCodeDetector.TryExtract(
            "Microsoft 账户安全代码",
            "请对个人 Microsoft 帐户使用以下安全代码。安全代码：704938。" +
            "Microsoft Corporation, One Microsoft Way, Redmond, WA 98052",
            out var code);

        Assert.True(ok);
        Assert.Equal("704938", code); // not the 98052 postcode in the footer
    }

    [Fact]
    public void Extracts_chinese_otp_after_colon()
    {
        bool ok = VerificationCodeDetector.TryExtract(
            "",
            "你好，当前操作需要输入验证码，请不要提供给他人，10分钟内有效：7740",
            out var code);

        Assert.True(ok);
        Assert.Equal("7740", code); // not the "10" minutes
    }

    [Fact]
    public void Extracts_english_code()
    {
        Assert.True(VerificationCodeDetector.TryExtract("Sign-in code", "Your verification code is 123456.", out var code));
        Assert.Equal("123456", code);
    }

    [Fact]
    public void Joins_a_space_grouped_code()
    {
        Assert.True(VerificationCodeDetector.TryExtract("", "Your one-time passcode is 123 456", out var code));
        Assert.Equal("123456", code);
    }

    [Fact]
    public void Returns_false_when_no_otp_keyword_present()
    {
        // Contains a number but nothing marking it as a verification code.
        Assert.False(VerificationCodeDetector.TryExtract("Your order shipped", "Tracking number 12345 is on its way.", out var code));
        Assert.Equal("", code);
    }

    [Fact]
    public void Returns_false_when_keyword_but_no_code()
    {
        Assert.False(VerificationCodeDetector.TryExtract("Verification code", "We could not generate your code right now.", out _));
    }

    [Theory]
    [InlineData("your verification code is 123")]        // 3 digits — too short
    [InlineData("your verification code is 123456789")]  // 9 digits — too long
    public void Rejects_out_of_range_code_lengths(string preview)
    {
        Assert.False(VerificationCodeDetector.TryExtract("", preview, out _));
    }

    [Fact]
    public void Handles_null_inputs()
    {
        Assert.False(VerificationCodeDetector.TryExtract(null, null, out var code));
        Assert.Equal("", code);
    }
}
