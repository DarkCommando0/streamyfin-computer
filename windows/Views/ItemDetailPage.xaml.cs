using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Streamyfin.Windows.Models;
using Streamyfin.Windows.ViewModels;

namespace Streamyfin.Windows.Views
{
    public sealed partial class ItemDetailPage : Page
    {
        // ── ViewModel ─────────────────────────────────────────────────────────────

        public ItemDetailViewModel ViewModel { get; }

        // ── Constructor ───────────────────────────────────────────────────────────

        public ItemDetailPage()
        {
            this.InitializeComponent();

            ViewModel        = new ItemDetailViewModel(App.JellyfinService);
            this.DataContext  = ViewModel;
        }

        // ── Navigation ────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by WinUI 3 when the frame navigates to this page.
        /// The parameter is the <see cref="MediaItem"/> clicked in HomePage.
        /// </summary>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            string? itemId = e.Parameter switch
            {
                MediaItem   mi  => mi.ItemId,
                string      id  => id,
                _               => null
            };

            if (string.IsNullOrWhiteSpace(itemId)) return;

            await ViewModel.LoadAsync(itemId);
        }

        // ── Play button ───────────────────────────────────────────────────────────

        /// <summary>
        /// Wired to the Play button's Click event in XAML.
        /// Passes a <see cref="PlaybackParameter"/> so MediaPlayerPage knows
        /// which item to play and where to resume.
        /// </summary>
        private void PlayButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (ViewModel.Item?.Id is null) return;

            var param = new PlaybackParameter
            {
                ItemId         = ViewModel.Item.Id,
                Title          = ViewModel.Title,
                StartTimeTicks = ViewModel.ResumeTicks,
                MediaSourceId  = ViewModel.Item.MediaSources?.FirstOrDefault()?.Id
            };

            Frame.Navigate(typeof(MediaPlayerPage), param);
        }

        // ── Similar item click ────────────────────────────────────────────────────

        private void SimilarList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is MediaItem item && !string.IsNullOrEmpty(item.ItemId))
                Frame.Navigate(typeof(ItemDetailPage), item);
        }
    }
}
