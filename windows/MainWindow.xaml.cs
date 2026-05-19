using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Streamyfin.Windows.Views;

namespace Streamyfin.Windows
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();

            // Set up Mica backdrop for Windows 11 visual styling
            this.SystemBackdrop = new MicaBackdrop();
            this.ExtendsContentIntoTitleBar = true;
            this.Title = "Streamyfin";

            // Set initial page to LoginPage
            RootFrame.Navigate(typeof(LoginPage));
        }
        
        // Expose a way for Pages to trigger a navigation on the RootFrame
        public void NavigateRoot(System.Type pageType)
        {
            RootFrame.Navigate(pageType);
        }
    }
}
