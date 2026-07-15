using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class NotificationActionPolicyTests
{
    [Fact]
    public void Incomplete_tasks_allow_open_snooze_and_complete()
        => Assert.Equal(NotificationActionMask.Open | NotificationActionMask.Snooze | NotificationActionMask.Complete, NotificationActionPolicy.GetActions(true, false));

    [Fact]
    public void Events_never_allow_complete()
        => Assert.Equal(NotificationActionMask.Open | NotificationActionMask.Snooze, NotificationActionPolicy.GetActions(false, false));

    [Fact]
    public void Date_only_tasks_are_due_at_nine_local_time()
        => Assert.Equal(new DateTime(2026, 7, 15, 9, 0, 0), NotificationActionPolicy.GetReminderTime(true, false, null, "Task", "2026-07-15"));
}
