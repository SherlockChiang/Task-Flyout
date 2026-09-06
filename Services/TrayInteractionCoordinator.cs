using System;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    // All entry points and continuations run on the UI dispatcher.
    internal sealed class TrayInteractionCoordinator(
        Func<Task> prepareFlyout,
        Action toggleFlyout,
        Func<Task> dismissFlyout,
        Action openMainWindow)
    {
        private long _mainWindowGeneration;
        private bool _openingMainWindow;

        public Task<bool> ToggleFlyoutAsync() => ToggleFlyoutAsync(toggleFlyout);

        public async Task<bool> ToggleFlyoutAsync(Action applyToggle)
        {
            if (_openingMainWindow) return false;
            long generation = _mainWindowGeneration;
            await prepareFlyout();
            if (generation != _mainWindowGeneration || _openingMainWindow)
                return false;

            applyToggle();
            return true;
        }

        public async Task OpenMainWindowAsync()
        {
            long generation = ++_mainWindowGeneration;
            _openingMainWindow = true;
            try
            {
                await dismissFlyout();
                if (generation == _mainWindowGeneration)
                    openMainWindow();
            }
            finally
            {
                if (generation == _mainWindowGeneration)
                    _openingMainWindow = false;
            }
        }
    }
}
