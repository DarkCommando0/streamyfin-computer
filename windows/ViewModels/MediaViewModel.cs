using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Streamyfin.Windows.Models;
using Streamyfin.Windows.Services;

namespace Streamyfin.Windows.ViewModels
{
    // ─────────────────────────────────────────────────────────────────────────────
    // MediaItem  —  the flat card model that XAML binds to
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A single card on the home screen.  All properties must be set before the
    /// item is added to an <see cref="ObservableCollection{T}"/> so XAML gets
    /// one binding notification rather than many incremental ones.
    /// </summary>
    public class MediaItem
    {
        /// <summary>Jellyfin Item Id — used to navigate to the detail page.</summary>
        public string  ItemId  { get; set; } = string.Empty;

        /// <summary>Display title (movie name, episode name, collection name…).</summary>
        public string  Title   { get; set; } = string.Empty;

        /// <summary>
        /// Secondary line below the title.
        /// • Episode  →  "S1:E3 · Series Name"
        /// • Movie    →  "2024"
        /// • Series   →  "2020 – 2023"
        /// • BoxSet   →  collection item count isn't shown; leave empty or use year range
        /// </summary>
        public string  Subtitle { get; set; } = string.Empty;

        /// <summary>
        /// Fully-qualified poster URL built by <see cref="JellyfinService.GetImageUrl"/>.
        /// Consumed by an <c>ImageEx</c> / <c>Image</c> control on the card.
        /// </summary>
        public string  ImageUrl { get; set; } = string.Empty;

        /// <summary>
        /// Ribbon / badge visibility logic:
        /// <list type="bullet">
        ///   <item>Continue Watching — always Visible (item is in-progress by definition)</item>
        ///   <item>Next Up           — always Visible (next unwatched episode)</item>
        ///   <item>Collections       — Visible when the collection contains unplayed items
        ///                            (approximated: UserData.PlayCount == 0)</item>
        ///   <item>Suggestions       — Visible when the item has never been played</item>
        /// </list>
        /// </summary>
        public Visibility RibbonVisibility { get; set; } = Visibility.Collapsed;

        /// <summary>
        /// Progress (0.0 – 1.0) through an in-progress item.
        /// Used to drive a thin progress bar at the bottom of the card.
        /// 0 when no progress data is available.
        /// </summary>
        public double PlaybackProgress { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // MediaViewModel
    // ─────────────────────────────────────────────────────────────────────────────

    public class MediaViewModel : INotifyPropertyChanged
    {
        // ── Observable collections ───────────────────────────────────────────────

        public ObservableCollection<MediaItem> ContinueWatchingItems { get; } = [];
        public ObservableCollection<MediaItem> NextUpItems           { get; } = [];
        public ObservableCollection<MediaItem> CollectionItems       { get; } = [];
        public ObservableCollection<MediaItem> SuggestionItems       { get; } = [];

        // ── Loading / error state ────────────────────────────────────────────────

        private bool _isLoading;
        /// <summary>
        /// True while <see cref="LoadHomeContentAsync"/> is running.
        /// Bind a <c>ProgressRing</c> or overlay to this.
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(); }
        }

        private string? _errorMessage;
        /// <summary>Non-null when the last load attempt produced an error.</summary>
        public string? ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value; OnPropertyChanged(); }
        }

        // ── Service reference ────────────────────────────────────────────────────

        private readonly JellyfinService _service;

        // ── Constructor ──────────────────────────────────────────────────────────

        public MediaViewModel(JellyfinService service)
        {
            _service = service;
        }

        // ── Home content loader ──────────────────────────────────────────────────

        /// <summary>
        /// Fetches all four home-screen rows from the Jellyfin API concurrently
        /// and populates the four <see cref="ObservableCollection{T}"/> properties.
        ///
        /// Call this from <c>HomePage.xaml.cs</c> once the page is loaded:
        /// <code>
        ///   await ViewModel.LoadHomeContentAsync();
        /// </code>
        /// </summary>
        public async Task LoadHomeContentAsync(CancellationToken ct = default)
        {
            IsLoading    = true;
            ErrorMessage = null;

            try
            {
                // Run the four API calls concurrently to minimise perceived load time.
                var continueTask    = _service.GetContinueWatchingAsync(limit: 12, ct: ct);
                var nextUpTask      = _service.GetNextUpAsync(ct: ct);
                var collectionsTask = _service.GetCollectionsAsync(limit: 12, ct: ct);
                var suggestionsTask = _service.GetLatestMoviesAsync(limit: 16, ct: ct);

                await Task.WhenAll(continueTask, nextUpTask, collectionsTask, suggestionsTask)
                    .ConfigureAwait(false);

                // Map and populate each collection on the UI thread.
                Populate(ContinueWatchingItems, continueTask.Result,    MapContinueWatching);
                Populate(NextUpItems,           nextUpTask.Result,       MapNextUp);
                Populate(CollectionItems,       collectionsTask.Result,  MapCollection);
                Populate(SuggestionItems,       suggestionsTask.Result,  MapSuggestion);
            }
            catch (JellyfinConnectionException ex)
            {
                ErrorMessage = $"Cannot reach server: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[MediaViewModel] {ex}");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not authenticated"))
            {
                ErrorMessage = "Session expired. Please log in again.";
            }
            catch (Exception ex)
            {
                ErrorMessage = "Failed to load content. Pull to refresh.";
                System.Diagnostics.Debug.WriteLine($"[MediaViewModel] Unexpected: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ── Mapping helpers ──────────────────────────────────────────────────────

        // Continue Watching — items are always in-progress, so ribbon is always shown.
        private MediaItem MapContinueWatching(BaseItemDto item)
        {
            var userData = item.UserData;
            double progress = ComputeProgress(item);

            string subtitle = item.Type == BaseItemKind.Episode
                ? $"S{item.ParentIndexNumber}:E{item.IndexNumber} · {item.SeriesName}"
                : item.ProductionYear?.ToString() ?? string.Empty;

            return new MediaItem
            {
                ItemId           = item.Id ?? string.Empty,
                Title            = item.Name ?? "Unknown",
                Subtitle         = subtitle,
                ImageUrl         = BuildImageUrl(item),
                RibbonVisibility = Visibility.Visible,  // always — it's in-progress
                PlaybackProgress = progress
            };
        }

        // Next Up — always unwatched episodes, ribbon always shown.
        private MediaItem MapNextUp(BaseItemDto item) => new()
        {
            ItemId           = item.Id ?? string.Empty,
            Title            = item.SeriesName ?? item.Name ?? "Unknown",
            Subtitle         = $"S{item.ParentIndexNumber}:E{item.IndexNumber} · {item.Name}",
            ImageUrl         = BuildImageUrl(item, preferBackdrop: true),
            RibbonVisibility = Visibility.Visible,
            PlaybackProgress = 0
        };

        // Collections (BoxSets) — show ribbon when collection has never been started.
        private MediaItem MapCollection(BaseItemDto item)
        {
            bool hasUnplayed = item.UserData?.PlayCount == 0;
            return new MediaItem
            {
                ItemId           = item.Id ?? string.Empty,
                Title            = item.Name ?? "Collection",
                Subtitle         = item.ProductionYear?.ToString() ?? string.Empty,
                ImageUrl         = BuildImageUrl(item),
                RibbonVisibility = hasUnplayed ? Visibility.Visible : Visibility.Collapsed,
                PlaybackProgress = 0
            };
        }

        // Suggestions / Latest — show ribbon when item has never been played.
        private MediaItem MapSuggestion(BaseItemDto item)
        {
            bool isUnwatched = item.UserData?.Played == false;
            return new MediaItem
            {
                ItemId           = item.Id ?? string.Empty,
                Title            = item.Name ?? "Unknown",
                Subtitle         = item.ProductionYear?.ToString() ?? string.Empty,
                ImageUrl         = BuildImageUrl(item),
                RibbonVisibility = isUnwatched ? Visibility.Visible : Visibility.Collapsed,
                PlaybackProgress = 0
            };
        }

        // ── Image URL construction ───────────────────────────────────────────────

        /// <summary>
        /// Builds a fully-qualified image URL for a <see cref="BaseItemDto"/>.
        /// For episodes, uses the series ID for the poster so we get the show art
        /// rather than a dark placeholder.
        /// </summary>
        private string BuildImageUrl(BaseItemDto item, bool preferBackdrop = false)
        {
            if (item.Id is null) return string.Empty;

            // Episodes: prefer the series poster for the card art.
            if (item.Type == BaseItemKind.Episode && item.SeriesId is not null && !preferBackdrop)
            {
                string? seriesTag = null; // tags are per-item; we omit it, server won't 404
                return _service.GetImageUrl(item.SeriesId, "Primary", seriesTag, maxWidth: 400);
            }

            // Prefer Backdrop for Next Up episodes (looks better in a wide card).
            if (preferBackdrop && item.BackdropImageTags?.Count > 0)
            {
                return _service.GetImageUrl(item.Id, "Backdrop",
                    item.BackdropImageTags[0], maxWidth: 500);
            }

            // Standard primary poster.
            string? tag = null;
            item.ImageTags?.TryGetValue("Primary", out tag);
            return _service.GetImageUrl(item.Id, "Primary", tag, maxWidth: 400);
        }

        // ── Progress calculation ─────────────────────────────────────────────────

        /// <summary>
        /// Returns a 0.0–1.0 value representing how far through the item the user is.
        /// Returns 0 when no progress data or runtime is available.
        /// </summary>
        private static double ComputeProgress(BaseItemDto item)
        {
            long? positionTicks = item.UserData?.PlaybackPositionTicks;
            long? runtimeTicks  = item.RunTimeTicks;

            if (!positionTicks.HasValue || !runtimeTicks.HasValue || runtimeTicks.Value <= 0)
                return 0;

            return Math.Clamp((double)positionTicks.Value / runtimeTicks.Value, 0.0, 1.0);
        }

        // ── Utility ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Clears a collection and re-populates it using a mapping function,
        /// keeping mutation minimal to avoid unnecessary UI redraws.
        /// </summary>
        private static void Populate<T>(
            ObservableCollection<MediaItem> collection,
            IEnumerable<T>                 items,
            Func<T, MediaItem>             map)
        {
            collection.Clear();
            foreach (var item in items)
                collection.Add(map(item));
        }

        // ── INotifyPropertyChanged ───────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
