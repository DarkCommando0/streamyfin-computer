using Microsoft.UI.Xaml.Controls;
using Streamyfin.Windows.ViewModels;

namespace Streamyfin.Windows.Views
{
    public sealed partial class HomePage : Page
    {
        public MediaViewModel ViewModel { get; }

        public HomePage()
        {
            this.InitializeComponent();

            // Construct the ViewModel, injecting the app-wide singleton service.
            ViewModel = new MediaViewModel(App.JellyfinService);

            // Set as DataContext so XAML bindings like {x:Bind ViewModel.IsLoading} work.
            this.DataContext = ViewModel;

            // Kick off the async load once the XAML tree is ready.
            this.Loaded += async (_, _) => await ViewModel.LoadHomeContentAsync();
        }

        // ── Item click — navigate to the detail page ─────────────────────────────

        private void MediaList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is MediaItem item)
            {
                // Pass the full MediaItem as navigation parameter so the detail
                // page can show something immediately while it loads from the API.
                Frame.Navigate(typeof(ItemDetailPage), item);
            }
        }
    }
}
