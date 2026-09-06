using System;
using Microsoft.UI.Xaml.Media;

namespace Task_Flyout.Services
{
    internal static class NextRenderHelper
    {
        // Subscribe and dispose on the UI thread. Rendering is not a presentation fence.
        public static IDisposable RunOnce(Action action) => new RenderSubscription(action);

        private sealed class RenderSubscription : IDisposable
        {
            private Action? _action;

            public RenderSubscription(Action action)
            {
                _action = action;
                CompositionTarget.Rendering += OnRendering;
            }

            private void OnRendering(object? sender, object args)
            {
                var action = _action;
                Dispose();
                action?.Invoke();
            }

            public void Dispose()
            {
                if (_action == null) return;
                CompositionTarget.Rendering -= OnRendering;
                _action = null;
            }
        }
    }
}
