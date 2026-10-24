using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class UnhandledExceptionPolicyTests
{
    [Fact]
    public void Allows_operation_cancellation_to_be_handled()
        => Assert.True(UnhandledExceptionPolicy.IsRecoverable(new OperationCanceledException()));

    [Fact]
    public void Allows_regex_timeout_to_be_handled()
        => Assert.True(UnhandledExceptionPolicy.IsRecoverable(new RegexMatchTimeoutException()));

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(CryptographicException))]
    public void Leaves_unknown_failures_unhandled(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.False(UnhandledExceptionPolicy.IsRecoverable(exception));
    }
}
