using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class TaskbarSelectionPolicyTests
{
    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    public void Recognizes_supported_taskbar_classes(string className)
        => Assert.True(TaskbarSelectionPolicy.IsSupportedClass(className));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TrayNotifyWnd")]
    public void Rejects_other_window_classes(string? className)
        => Assert.False(TaskbarSelectionPolicy.IsSupportedClass(className));

    [Fact]
    public void Preserves_current_valid_supported_taskbar()
        => Assert.Equal(22, TaskbarSelectionPolicy.Select(22, true, "Shell_SecondaryTrayWnd", 11, 33));

    [Theory]
    [InlineData(22, false, "Shell_SecondaryTrayWnd")]
    [InlineData(22, true, "OtherWindow")]
    [InlineData(0, true, "Shell_TrayWnd")]
    public void Falls_back_to_primary_when_current_is_not_usable(long current, bool valid, string currentClass)
        => Assert.Equal(11, TaskbarSelectionPolicy.Select(current, valid, currentClass, 11, 33));

    [Fact]
    public void Uses_secondary_when_primary_is_missing()
        => Assert.Equal(33, TaskbarSelectionPolicy.Select(0, false, null, 0, 33));

    [Fact]
    public void Returns_zero_when_no_taskbar_exists()
        => Assert.Equal(0, TaskbarSelectionPolicy.Select(0, false, null, 0, 0));
}
