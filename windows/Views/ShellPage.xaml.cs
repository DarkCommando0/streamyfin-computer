using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Streamyfin.Windows.Views
{
    public sealed partial class ShellPage : Page
    {
        public ShellPage()
        {
            this.InitializeComponent();
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            // Set default view to home
            ContentFrame.Navigate(typeof(HomePage));
            NavView.SelectedItem = NavView.MenuItems[0];
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                // Settings routing
                return;
            }

            var tag = args.InvokedItemContainer.Tag?.ToString();
            switch (tag)
            {
                case "HomePage":
                    ContentFrame.Navigate(typeof(HomePage));
                    break;
                case "SearchPage":
                    ContentFrame.Navigate(typeof(SearchPage));
                    break;
                case "LibraryPage":
                    ContentFrame.Navigate(typeof(LibraryPage));
                    break;
                case "DownloadsPage":
                    ContentFrame.Navigate(typeof(DownloadsPage));
                    break;
            }
        }
    }
}
