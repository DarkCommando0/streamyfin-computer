using Microsoft.UI.Xaml;
using Streamyfin.Windows.Services;

namespace Streamyfin.Windows
{
    public partial class App : Application
    {
        private Window? m_window;

        /// <summary>
        /// Singleton Jellyfin service shared across all pages.
        /// Access via <c>App.JellyfinService</c> from any code-behind or ViewModel.
        /// </summary>
        public static readonly JellyfinService JellyfinService =
            new(new System.Net.Http.HttpClient());

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            m_window.Activate();
        }
    }
}
