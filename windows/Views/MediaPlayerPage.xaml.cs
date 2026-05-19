using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Streamyfin.Windows.Models;
using Streamyfin.Windows.Services;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Streamyfin.Windows.Views
{
    public sealed partial class MediaPlayerPage : Page
    {
        // ─────────────────────────────────────────────────────────────────────────
        // Constants
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// How often to report progress back to the Jellyfin server.
        /// Jellyfin recommends every 10 seconds.
        /// </summary>
        private static readonly TimeSpan ProgressReportInterval = TimeSpan.FromSeconds(10);

        // ─────────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────────

        private readonly JellyfinService _service;
        private readonly MediaPlayer     _mediaPlayer;

        // Session info stored after GetStreamUrlAsync resolves
        private string? _playSessionId;
        private string? _mediaSourceId;
        private string? _itemId;

        // Periodic progress reporter
        private DispatcherTimer? _progressTimer;

        // CancellationToken for the initial stream URL fetch
        private CancellationTokenSource _cts = new();

        // ─────────────────────────────────────────────────────────────────────────
        // Constructor
        // ─────────────────────────────────────────────────────────────────────────

        public MediaPlayerPage()
        {
            this.InitializeComponent();

            _service     = App.JellyfinService;
            _mediaPlayer = new MediaPlayer { AutoPlay = false };

            Player.SetMediaPlayer(_mediaPlayer);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Navigation
        // ─────────────────────────────────────────────────────────────────────────

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is not PlaybackParameter param ||
                string.IsNullOrWhiteSpace(param.ItemId))
            {
                System.Diagnostics.Debug.WriteLine(
                    "[MediaPlayerPage] Invalid or missing PlaybackParameter.");
                return;
            }

            _itemId = param.ItemId;

            await StartPlaybackAsync(param);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _ = CleanupAsync();   // fire-and-forget; reports stop to server then disposes
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Playback start
        // ─────────────────────────────────────────────────────────────────────────

        private async Task StartPlaybackAsync(PlaybackParameter param)
        {
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            try
            {
                // ── 1. Resolve the stream URL from the server ─────────────────────
                var result = await _service.GetStreamUrlAsync(
                    item: await _service.GetItemAsync(param.ItemId, ct)
                          ?? throw new InvalidOperationException("Item not found."),
                    startTimeTicks:      param.StartTimeTicks,
                    mediaSourceId:       param.MediaSourceId,
                    ct:                  ct);

                if (result is null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[MediaPlayerPage] GetStreamUrlAsync returned null.");
                    return;
                }

                _playSessionId = result.SessionId;
                _mediaSourceId = result.MediaSource?.Id ?? param.MediaSourceId;

                System.Diagnostics.Debug.WriteLine(
                    $"[MediaPlayerPage] Playing: {result.Url}  transcoded={result.IsTranscoded}");

                // ── 2. Set media source ────────────────────────────────────────────
                var source = MediaSource.CreateFromUri(new Uri(result.Url));
                _mediaPlayer.Source = source;

                // ── 3. Seek to resume position (after source is set) ──────────────
                if (param.StartTimeTicks > 0)
                {
                    _mediaPlayer.PlaybackSession.Position =
                        TimeSpan.FromTicks(param.StartTimeTicks);
                }

                // ── 4. Report playback start to server ────────────────────────────
                await _service.ReportPlaybackStartAsync(BuildProgressInfo(isPaused: false), ct);

                // ── 5. Begin periodic progress timer ──────────────────────────────
                StartProgressTimer();

                // ── 6. Start playing ──────────────────────────────────────────────
                _mediaPlayer.Play();
            }
            catch (OperationCanceledException)
            {
                // User navigated away before stream resolved — normal, do nothing.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MediaPlayerPage] StartPlaybackAsync failed: {ex}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Progress reporting
        // ─────────────────────────────────────────────────────────────────────────

        private void StartProgressTimer()
        {
            _progressTimer          = new DispatcherTimer();
            _progressTimer.Interval = ProgressReportInterval;
            _progressTimer.Tick    += OnProgressTick;
            _progressTimer.Start();
        }

        private async void OnProgressTick(object? sender, object e)
        {
            if (_itemId is null) return;

            bool isPaused = _mediaPlayer.PlaybackSession.PlaybackState
                            == MediaPlaybackState.Paused;

            try
            {
                await _service.ReportPlaybackProgressAsync(
                    BuildProgressInfo(isPaused));
            }
            catch (Exception ex)
            {
                // Non-critical — log and continue. Don't crash the player.
                System.Diagnostics.Debug.WriteLine(
                    $"[MediaPlayerPage] Progress report failed: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Cleanup
        // ─────────────────────────────────────────────────────────────────────────

        private async Task CleanupAsync()
        {
            // Cancel any in-flight stream resolve.
            await _cts.CancelAsync();

            // Stop the progress timer.
            _progressTimer?.Stop();
            _progressTimer = null;

            // Report playback stopped to the server so the resume position is saved.
            if (_itemId is not null)
            {
                try
                {
                    await _service.StopPlaybackAsync(new PlaybackStopInfo
                    {
                        ItemId        = _itemId,
                        MediaSourceId = _mediaSourceId,
                        PlaySessionId = _playSessionId,
                        PositionTicks = (long)_mediaPlayer.PlaybackSession.Position.Ticks,
                        Failed        = false
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[MediaPlayerPage] StopPlayback failed: {ex.Message}");
                }
            }

            // Release the native media player resources.
            _mediaPlayer.Pause();
            _mediaPlayer.Source = null;
            _mediaPlayer.Dispose();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helper — build progress info from current player state
        // ─────────────────────────────────────────────────────────────────────────

        private PlaybackProgressInfo BuildProgressInfo(bool isPaused) => new()
        {
            ItemId        = _itemId,
            MediaSourceId = _mediaSourceId,
            PlaySessionId = _playSessionId,
            PositionTicks = (long)_mediaPlayer.PlaybackSession.Position.Ticks,
            IsPaused      = isPaused,
            IsMuted       = _mediaPlayer.IsMuted,
            VolumeLevel   = (int)(_mediaPlayer.Volume * 100),
            CanSeek       = true,
            PlayMethod    = "DirectStream"  // updated to Transcode if needed via result.IsTranscoded
        };
    }
}
