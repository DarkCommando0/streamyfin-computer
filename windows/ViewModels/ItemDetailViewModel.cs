using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Streamyfin.Windows.Models;
using Streamyfin.Windows.Services;

namespace Streamyfin.Windows.ViewModels
{
    /// <summary>
    /// ViewModel for ItemDetailPage.
    /// Loads the full item, its cast/crew, and similar title suggestions
    /// from the Jellyfin API, then exposes them for XAML binding.
    /// </summary>
    public class ItemDetailViewModel : INotifyPropertyChanged
    {
        // ── Service ──────────────────────────────────────────────────────────────

        private readonly JellyfinService _service;

        // ── Backing fields ───────────────────────────────────────────────────────

        private BaseItemDto?  _item;
        private bool          _isLoading;
        private string?       _errorMessage;

        // ── Observable properties ────────────────────────────────────────────────

        /// <summary>Full item returned by the server (title, overview, streams…).</summary>
        public BaseItemDto? Item
        {
            get => _item;
            private set { _item = value; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Overview)); OnPropertyChanged(nameof(YearLabel)); OnPropertyChanged(nameof(RuntimeLabel)); OnPropertyChanged(nameof(BackdropUrl)); OnPropertyChanged(nameof(VideoStreamLabel)); OnPropertyChanged(nameof(AudioStreamLabel)); OnPropertyChanged(nameof(SubtitleStreamLabel)); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(); }
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value; OnPropertyChanged(); }
        }

        // ── Derived display properties (computed from Item) ───────────────────────

        public string Title        => _item?.Name       ?? string.Empty;
        public string Overview     => _item?.Overview   ?? string.Empty;
        public string YearLabel    => _item?.ProductionYear?.ToString() ?? string.Empty;
        public string RuntimeLabel => FormatRuntime(_item?.RunTimeTicks);
        public string BackdropUrl  => BuildBackdropUrl();

        // ── Stream info labels (for the metadata grid) ────────────────────────────

        public string VideoStreamLabel    => GetVideoStreamLabel();
        public string AudioStreamLabel    => GetAudioStreamLabel();
        public string SubtitleStreamLabel => GetSubtitleStreamLabel();

        // ── Collections ──────────────────────────────────────────────────────────

        /// <summary>Cast and crew — bind to a horizontal people list.</summary>
        public ObservableCollection<PersonInfo> People { get; } = [];

        /// <summary>"More Like This" row at the bottom of the page.</summary>
        public ObservableCollection<MediaItem> SimilarItems { get; } = [];

        // ── Resume position ──────────────────────────────────────────────────────

        /// <summary>
        /// Resume position in ticks.  Passed to MediaPlayerPage so playback
        /// starts from where the user left off.
        /// </summary>
        public long ResumeTicks => _item?.UserData?.PlaybackPositionTicks ?? 0;

        // ── Constructor ───────────────────────────────────────────────────────────

        public ItemDetailViewModel(JellyfinService service)
        {
            _service = service;
        }

        // ── Loader ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fetches the full item details, people, and similar items concurrently.
        /// Safe to call multiple times (re-loads fresh data each call).
        /// </summary>
        public async Task LoadAsync(string itemId, CancellationToken ct = default)
        {
            IsLoading    = true;
            ErrorMessage = null;

            try
            {
                // Run all three requests in parallel.
                var itemTask    = _service.GetItemAsync(itemId, ct);
                var peopleTask  = _service.GetPeopleAsync(itemId, ct);
                var similarTask = _service.GetSimilarItemsAsync(itemId, limit: 12, ct: ct);

                await Task.WhenAll(itemTask, peopleTask, similarTask)
                    .ConfigureAwait(false);

                Item = itemTask.Result;

                People.Clear();
                foreach (var person in peopleTask.Result)
                    People.Add(person);

                SimilarItems.Clear();
                foreach (var similar in similarTask.Result)
                    SimilarItems.Add(MapSimilar(similar));
            }
            catch (JellyfinConnectionException ex)
            {
                ErrorMessage = $"Cannot reach server: {ex.Message}";
            }
            catch (Exception ex)
            {
                ErrorMessage = "Failed to load item details.";
                System.Diagnostics.Debug.WriteLine($"[ItemDetailViewModel] {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ── Mapping ───────────────────────────────────────────────────────────────

        private MediaItem MapSimilar(BaseItemDto dto) => new()
        {
            ItemId           = dto.Id ?? string.Empty,
            Title            = dto.Name ?? "Unknown",
            Subtitle         = dto.ProductionYear?.ToString() ?? string.Empty,
            ImageUrl         = BuildPosterUrl(dto),
            RibbonVisibility = (dto.UserData?.Played == false)
                                   ? Microsoft.UI.Xaml.Visibility.Visible
                                   : Microsoft.UI.Xaml.Visibility.Collapsed,
            PlaybackProgress = 0
        };

        // ── Image URL helpers ─────────────────────────────────────────────────────

        private string BuildBackdropUrl()
        {
            if (_item?.Id is null) return string.Empty;

            if (_item.BackdropImageTags?.Count > 0)
                return _service.GetImageUrl(_item.Id, "Backdrop",
                    _item.BackdropImageTags[0], maxWidth: 1280);

            // Fall back to Primary if no backdrop exists.
            string? tag = null;
            _item.ImageTags?.TryGetValue("Primary", out tag);
            return _service.GetImageUrl(_item.Id, "Primary", tag, maxWidth: 1280);
        }

        private string BuildPosterUrl(BaseItemDto dto)
        {
            if (dto.Id is null) return string.Empty;
            if (dto.Type == BaseItemKind.Episode && dto.SeriesId is not null)
                return _service.GetImageUrl(dto.SeriesId, "Primary", maxWidth: 300);
            string? tag = null;
            dto.ImageTags?.TryGetValue("Primary", out tag);
            return _service.GetImageUrl(dto.Id, "Primary", tag, maxWidth: 300);
        }

        // ── Stream info helpers ───────────────────────────────────────────────────

        private string GetVideoStreamLabel()
        {
            var vs = _item?.MediaSources?
                .FirstOrDefault()?.MediaStreams?
                .FirstOrDefault(s => s.Type == MediaStreamType.Video);

            if (vs is null) return "Unknown";

            string codec     = vs.Codec?.ToUpperInvariant() ?? "?";
            string height    = vs.Height.HasValue ? $"{vs.Height}p" : string.Empty;
            string videoRange = vs.VideoRange ?? string.Empty;

            return string.Join(" ", new[] { height, codec, videoRange }
                .Where(s => !string.IsNullOrEmpty(s)));
        }

        private string GetAudioStreamLabel()
        {
            var source = _item?.MediaSources?.FirstOrDefault();
            if (source is null) return "Unknown";

            int defaultIndex = source.DefaultAudioStreamIndex ?? -1;
            var audioStream = source.MediaStreams?
                .FirstOrDefault(s => s.Type == MediaStreamType.Audio &&
                                     (defaultIndex < 0 || s.Index == defaultIndex));

            if (audioStream is null) return "Unknown";

            string lang     = audioStream.Language?.ToUpperInvariant() ?? "?";
            string codec    = audioStream.Codec?.ToUpperInvariant() ?? "?";
            string channels = audioStream.Channels.HasValue
                ? $"{audioStream.Channels}.1" : string.Empty;

            return $"{lang} - {codec}{(channels.Length > 0 ? " - " + channels : string.Empty)}";
        }

        private string GetSubtitleStreamLabel()
        {
            var source = _item?.MediaSources?.FirstOrDefault();
            if (source is null) return "None";

            int defaultIndex = source.DefaultSubtitleStreamIndex ?? -1;
            if (defaultIndex < 0) return "None";

            var sub = source.MediaStreams?
                .FirstOrDefault(s => s.Type == MediaStreamType.Subtitle &&
                                     s.Index == defaultIndex);

            if (sub is null) return "None";

            string lang    = sub.Language?.ToUpperInvariant() ?? "?";
            string codec   = sub.Codec?.ToUpperInvariant() ?? "?";
            string ext     = sub.IsExternal ? "External" : "Embedded";

            return $"{lang} - {codec} - {ext}";
        }

        // ── Utility ───────────────────────────────────────────────────────────────

        private static string FormatRuntime(long? ticks)
        {
            if (!ticks.HasValue || ticks.Value <= 0) return string.Empty;

            var ts = TimeSpan.FromTicks(ticks.Value);
            return ts.Hours > 0
                ? $"{ts.Hours}h {ts.Minutes:D2}m"
                : $"{ts.Minutes}m";
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
