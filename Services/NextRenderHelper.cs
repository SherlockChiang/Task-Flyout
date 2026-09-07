using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Task_Flyout.Services
{
    internal static class NextRenderHelper
    {
        // Subscribe and dispose on the UI thread. Rendering is not a presentation fence.
        public static IDisposable RunOnce(Action action) => new RenderSubscription(action);

        public static IDisposable RunOnceWhen(Func<bool> condition, Action action, Action<string> skipped)
            => new RenderSubscription(action, condition, skipped);

        private sealed class RenderSubscription : IDisposable
        {
            private Action? _action;
            private Func<bool>? _condition;
            private Action<string>? _skipped;
            private readonly DispatcherTimer? _timeout;

            public RenderSubscription(Action action, Func<bool>? condition = null, Action<string>? skipped = null)
            {
                _action = action;
                _condition = condition;
                _skipped = skipped;
                CompositionTarget.Rendering += OnRendering;
                if (condition != null)
                {
                    _timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                    _timeout.Tick += OnTimeout;
                    _timeout.Start();
                }
            }

            private void OnRendering(object? sender, object args)
            {
                if (_action == null) return;
                try { if (_condition != null && !_condition()) return; }
                catch { Skip("unavailable"); return; }
                var action = _action;
                Dispose();
                action?.Invoke();
            }

            private void OnTimeout(object? sender, object args) => Skip("timeout");

            private void Skip(string reason)
            {
                if (_action == null) return;
                var skipped = _skipped;
                Dispose();
                skipped?.Invoke(reason);
            }

            public void Dispose()
            {
                if (_action == null) return;
                CompositionTarget.Rendering -= OnRendering;
                if (_timeout != null)
                {
                    _timeout.Stop();
                    _timeout.Tick -= OnTimeout;
                }
                _action = null;
                _condition = null;
                _skipped = null;
            }
        }
    }
}
