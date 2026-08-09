using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed class WeatherCompanionCoordinator : IAsyncDisposable
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan MaxSnapshotAge = TimeSpan.FromHours(2);

        private readonly WeatherService _weatherService;
        private readonly Func<bool> _queueOpenWeather;
        private readonly object _snapshotLock = new();
        private readonly SemaphoreSlim _refreshSignal = new(0, 1);
        private WeatherCompanionServer? _server;
        private CancellationTokenSource? _shutdown;
        private Task? _refreshLoop;
        private WeatherCompanionSnapshot? _snapshot;
        private int _refreshSignalPending;
        private int _forceRefresh;
        private bool _started;
        private bool _enabled;

        public WeatherCompanionCoordinator(
            WeatherService weatherService,
            Func<bool> queueOpenWeather)
        {
            _weatherService = weatherService ?? throw new ArgumentNullException(nameof(weatherService));
            _queueOpenWeather = queueOpenWeather ?? throw new ArgumentNullException(nameof(queueOpenWeather));
        }

        public void Start()
        {
            if (_started) return;
            var shutdown = new CancellationTokenSource();
            var server = new WeatherCompanionServer(HandleRequestAsync);
            server.Start();

            _shutdown = shutdown;
            _server = server;
            _weatherService.LocationUpdated += WeatherService_Changed;
            _weatherService.LocationsChanged += WeatherService_Changed;
            _started = true;
            _refreshLoop = Task.Run(() => RefreshLoopAsync(shutdown.Token));
        }

        public void SetEnabled(bool enabled)
        {
            if (!_started) return;
            _enabled = enabled;
            if (!enabled)
            {
                lock (_snapshotLock) _snapshot = null;
                return;
            }

            RequestRefresh(forceRefresh: false);
        }

        public void RequestRefresh(bool forceRefresh)
        {
            if (!_started) return;
            if (forceRefresh) Interlocked.Exchange(ref _forceRefresh, 1);
            if (Interlocked.Exchange(ref _refreshSignalPending, 1) == 0)
                _refreshSignal.Release();
        }

        public async ValueTask DisposeAsync()
        {
            if (!_started) return;
            _started = false;
            _enabled = false;
            _weatherService.LocationUpdated -= WeatherService_Changed;
            _weatherService.LocationsChanged -= WeatherService_Changed;
            lock (_snapshotLock) _snapshot = null;

            CancellationTokenSource? shutdown = _shutdown;
            _shutdown = null;
            shutdown?.Cancel();

            if (_refreshLoop != null)
            {
                try { await _refreshLoop.ConfigureAwait(false); }
                catch (OperationCanceledException) when (shutdown?.IsCancellationRequested == true) { }
            }

            if (_server != null)
            {
                await _server.DisposeAsync().ConfigureAwait(false);
                _server = null;
            }

            _refreshLoop = null;
            shutdown?.Dispose();
            _refreshSignal.Dispose();
        }

        private void WeatherService_Changed(object? sender, EventArgs e)
            => RequestRefresh(forceRefresh: true);

        private async Task RefreshLoopAsync(CancellationToken cancellationToken)
        {
            DateTime nextRefreshUtc = DateTime.MinValue;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TimeSpan delay = nextRefreshUtc - DateTime.UtcNow;
                    if (delay > TimeSpan.Zero)
                    {
                        if (await WaitForSignalOrDelayAsync(delay, cancellationToken).ConfigureAwait(false))
                            Interlocked.Exchange(ref _refreshSignalPending, 0);
                    }
                    else
                    {
                        await _refreshSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                        Interlocked.Exchange(ref _refreshSignalPending, 0);
                    }

                    while (_refreshSignal.Wait(0))
                        Interlocked.Exchange(ref _refreshSignalPending, 0);

                    if (_enabled)
                    {
                        bool forceRefresh = Interlocked.Exchange(ref _forceRefresh, 0) != 0;
                        await RefreshSnapshotAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
                    }

                    nextRefreshUtc = DateTime.UtcNow + RefreshInterval;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task<bool> WaitForSignalOrDelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            using var signalCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task signalTask = _refreshSignal.WaitAsync(signalCancellation.Token);
            Task delayTask = Task.Delay(delay, cancellationToken);
            Task completed = await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);
            signalCancellation.Cancel();

            if (completed == signalTask)
            {
                await signalTask.ConfigureAwait(false);
                return true;
            }

            try { await signalTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (signalCancellation.IsCancellationRequested) { }
            return false;
        }

        private async Task RefreshSnapshotAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            if (!_enabled || !_weatherService.IsEnabled)
            {
                lock (_snapshotLock) _snapshot = null;
                return;
            }

            try
            {
                await _weatherService.GetWeatherAsync(
                    forceRefresh,
                    cancellationToken).ConfigureAwait(false);
                var state = _weatherService.GetActiveCachedWeatherState();
                WeatherInfo? info = state.Info;
                if (info == null)
                {
                    lock (_snapshotLock) _snapshot = null;
                    return;
                }

                WeatherAlert? alert = null;
                if (_weatherService.BarAlertsEnabled)
                {
                    try { alert = _weatherService.DetectUpcomingAlert(info); }
                    catch (Exception ex) { Debug.WriteLine($"Weather companion alert update failed: {ex.Message}"); }
                }

                WeatherCompanionSnapshot snapshot = WeatherCompanionProtocol.CreateSnapshot(
                    alert?.Icon ?? info.Icon,
                    info.Temperature,
                    info.Description,
                    info.City,
                    alert == null ? string.Empty : _weatherService.FormatBarAlert(alert),
                    state.UpdatedUtc);
                lock (_snapshotLock) _snapshot = snapshot;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Keep the last good snapshot. Its timestamp lets the native client
                // fail open when the data becomes too old.
                Debug.WriteLine($"Weather companion refresh failed: {ex.Message}");
            }
        }

        private ValueTask<byte[]> HandleRequestAsync(
            WeatherCompanionRequest request,
            CancellationToken cancellationToken)
        {
            if (!_enabled)
            {
                return ValueTask.FromResult(WeatherCompanionProtocol.SerializeResponse(
                    WeatherCompanionResponseStatus.Unavailable,
                    detail: "bridge-disabled"));
            }

            return request.Command switch
            {
                WeatherCompanionCommand.Ping => ValueTask.FromResult(
                    WeatherCompanionProtocol.SerializeResponse(
                        WeatherCompanionResponseStatus.Ok,
                        detail: "ready")),
                WeatherCompanionCommand.GetSnapshot => GetSnapshotResponse(),
                WeatherCompanionCommand.OpenWeather => OpenWeatherResponse(),
                _ => ValueTask.FromResult(WeatherCompanionProtocol.SerializeResponse(
                    WeatherCompanionResponseStatus.InvalidRequest,
                    detail: "unsupported-command"))
            };
        }

        private ValueTask<byte[]> GetSnapshotResponse()
        {
            WeatherCompanionSnapshot? snapshot;
            lock (_snapshotLock) snapshot = _snapshot;
            if (snapshot is not WeatherCompanionSnapshot value)
            {
                return ValueTask.FromResult(WeatherCompanionProtocol.SerializeResponse(
                    WeatherCompanionResponseStatus.Unavailable,
                    detail: "weather-cache-empty"));
            }

            return ValueTask.FromResult(WeatherCompanionProtocol.IsSnapshotFresh(
                    value,
                    DateTimeOffset.UtcNow,
                    MaxSnapshotAge)
                ? WeatherCompanionProtocol.SerializeResponse(
                    WeatherCompanionResponseStatus.Ok,
                    value)
                : WeatherCompanionProtocol.SerializeResponse(
                    WeatherCompanionResponseStatus.Unavailable,
                    detail: "weather-cache-stale"));
        }

        private ValueTask<byte[]> OpenWeatherResponse()
            => ValueTask.FromResult(_queueOpenWeather()
                ? WeatherCompanionProtocol.SerializeResponse(WeatherCompanionResponseStatus.Ok)
                : WeatherCompanionProtocol.SerializeResponse(
                    WeatherCompanionResponseStatus.Unavailable,
                    detail: "app-closing"));
    }
}
