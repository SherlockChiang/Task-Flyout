using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Task_Flyout.Services;

namespace Task_Flyout.Tests
{
    [CollectionDefinition("Rendering stub", DisableParallelization = true)]
    public sealed class RenderingStubCollection { }

    [Collection("Rendering stub")]
    public sealed class NextRenderHelperTests
    {
        [Fact]
        public void Existing_once_behavior_unsubscribes_before_calling_action()
        {
            int calls = 0;
            using var observation = NextRenderHelper.RunOnce(() =>
            {
                Assert.Equal(0, CompositionTarget.SubscriberCount);
                calls++;
            });
            CompositionTarget.Render();
            CompositionTarget.Render();
            Assert.Equal(1, calls);
        }

        [Fact]
        public void Hidden_host_frames_are_ignored_until_visibility_is_reported()
        {
            bool visible = false;
            int calls = 0;
            using var observation = NextRenderHelper.RunOnceWhen(() => visible, () => calls++, _ => Assert.Fail("Unexpected skipped render."));
            CompositionTarget.Render();
            Assert.Equal(0, calls);
            visible = true;
            CompositionTarget.Render();
            CompositionTarget.Render();
            Assert.Equal(1, calls);
            Assert.Equal(0, CompositionTarget.SubscriberCount);
            Assert.False(DispatcherTimer.LastCreated!.IsEnabled);
        }

        [Fact]
        public void Dispose_cancels_callbacks_including_an_already_captured_event()
        {
            int calls = 0;
            var observation = NextRenderHelper.RunOnceWhen(() => true, () => calls++, _ => calls++);
            var staleHandler = CompositionTarget.Snapshot();
            observation.Dispose();
            observation.Dispose();
            staleHandler?.Invoke(null, new object());
            DispatcherTimer.LastCreated!.TickNow();
            Assert.Equal(0, calls);
            Assert.Equal(0, CompositionTarget.SubscriberCount);
        }

        [Fact]
        public void Timeout_is_reported_once_and_prevents_late_render_success()
        {
            var outcomes = new List<string>();
            using var observation = NextRenderHelper.RunOnceWhen(() => false, () => outcomes.Add("success"), outcomes.Add);
            var timer = DispatcherTimer.LastCreated!;
            Assert.Equal(TimeSpan.FromSeconds(5), timer.Interval);
            timer.TickNow();
            timer.TickNow();
            CompositionTarget.Render();
            Assert.Equal(new[] { "timeout" }, outcomes);
            Assert.Equal(0, CompositionTarget.SubscriberCount);
            Assert.False(timer.IsEnabled);
        }

        [Fact]
        public void Visibility_probe_failure_does_not_escape_to_the_ui()
        {
            var outcomes = new List<string>();
            using var observation = NextRenderHelper.RunOnceWhen(
                () => throw new InvalidOperationException(), () => outcomes.Add("success"), outcomes.Add);
            CompositionTarget.Render();
            Assert.Equal(new[] { "unavailable" }, outcomes);
            Assert.Equal(0, CompositionTarget.SubscriberCount);
        }
    }
}

// Manual framework event sources for testing the production subscription logic.
// These do not create or automate any native Windows UI.
namespace Microsoft.UI.Xaml.Media
{
    internal static class CompositionTarget
    {
        public static event EventHandler<object>? Rendering;
        public static int SubscriberCount => Rendering?.GetInvocationList().Length ?? 0;
        public static EventHandler<object>? Snapshot() => Rendering;
        public static void Render() => Rendering?.Invoke(null, new object());
    }
}

namespace Microsoft.UI.Xaml
{
    internal sealed class DispatcherTimer
    {
        public static DispatcherTimer? LastCreated { get; private set; }
        public DispatcherTimer() => LastCreated = this;
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; private set; }
        public event EventHandler<object>? Tick;
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void TickNow() { if (IsEnabled) Tick?.Invoke(this, new object()); }
    }
}
