using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class GoogleItemContainerPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_calendar_id_falls_back_to_primary(string? calendarId)
        => Assert.Equal("primary", GoogleItemContainerPolicy.ResolveCalendarId(calendarId));

    [Fact]
    public void Source_calendar_id_is_preserved()
        => Assert.Equal(
            "team@example.com",
            GoogleItemContainerPolicy.ResolveCalendarId("team@example.com"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_task_list_id_falls_back_to_default(string? taskListId)
        => Assert.Equal("@default", GoogleItemContainerPolicy.ResolveTaskListId(taskListId));

    [Fact]
    public void Source_task_list_id_is_preserved()
        => Assert.Equal("work-list", GoogleItemContainerPolicy.ResolveTaskListId("work-list"));
}
