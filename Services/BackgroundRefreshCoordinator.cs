using Microsoft.UI.Dispatching;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed class BackgroundRefreshCoordinator
    {
        private readonly DispatcherQueue _dispatcher;
        private readonly NotificationService _notifications;
        private readonly MailService _mail;
        private DispatcherQueueTimer? _timer;
        private int _running;

        public BackgroundRefreshCoordinator(DispatcherQueue dispatcher, NotificationService notifications, MailService mail)
        {
            _dispatcher = dispatcher;
            _notifications = notifications;
            _mail = mail;
        }

        public void Start()
        {
            _timer ??= _dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMinutes(1);
            _timer.IsRepeating = true;
            _timer.Tick -= Timer_Tick;
            _timer.Tick += Timer_Tick;
            if (!_timer.IsRunning) _timer.Start();
            _ = RunDueWorkAsync();
        }

        public void Stop() => _timer?.Stop();

        public Task RunNowAsync() => RunDueWorkAsync();

        private async void Timer_Tick(DispatcherQueueTimer sender, object args)
            => await RunDueWorkAsync();

        private async Task RunDueWorkAsync()
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
            try
            {
                try { _notifications.CheckUpcomingEvents(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Notification heartbeat failed: {ex.Message}"); }
                try { await _mail.RunScheduledPollIfDueAsync(DateTimeOffset.UtcNow); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Mail heartbeat failed: {ex.Message}"); }
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        }
    }
}
