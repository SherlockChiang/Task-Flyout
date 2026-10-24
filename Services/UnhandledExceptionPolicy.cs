using System;
using System.Text.RegularExpressions;

namespace Task_Flyout.Services;

internal static class UnhandledExceptionPolicy
{
    public static bool IsRecoverable(Exception exception)
        => exception is RegexMatchTimeoutException or OperationCanceledException;
}
