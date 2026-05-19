using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Streamyfin.Windows.Services;

namespace Streamyfin.Windows.Views
{
    public sealed partial class LoginPage : Page
    {
        public LoginPage()
        {
            this.InitializeComponent();
        }

        // ── Login button handler ─────────────────────────────────────────────────

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            // ── 1. Validate inputs ───────────────────────────────────────────────
            string serverUrl = ServerUrlBox.Text.Trim();
            string username  = UsernameBox.Text.Trim();
            string password  = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(serverUrl) ||
                string.IsNullOrWhiteSpace(username))
            {
                await ShowErrorAsync("Please enter a server URL and username.");
                return;
            }

            // Normalise: ensure the URL has a scheme.
            if (!serverUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                serverUrl = "https://" + serverUrl;
            }

            // ── 2. Update UI for loading state ───────────────────────────────────
            SetBusy(true);

            try
            {
                var service = App.JellyfinService;

                var result = await service.LoginAsync(serverUrl, username, password);

                if (result is null)
                {
                    // Null = 401 Unauthorized — bad credentials, service didn't throw.
                    await ShowErrorAsync("Invalid username or password. Please try again.");
                    return;
                }

                // ── 3. Persist session so user stays logged in after restart ─────
                service.SaveSession();

                // ── 4. Announce capabilities to the server ───────────────────────
                // Fire-and-forget — non-critical, don't block navigation.
                _ = service.PostCapabilitiesAsync();

                // ── 5. Navigate to the main shell ────────────────────────────────
                Frame.Navigate(typeof(ShellPage));
            }
            catch (JellyfinConnectionException ex)
            {
                await ShowErrorAsync(
                    $"Could not reach the server.\n\n" +
                    $"Check that the URL is correct and the server is online.\n\n" +
                    $"Detail: {ex.Message}");
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"An unexpected error occurred:\n{ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[LoginPage] Unexpected login error: {ex}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>Enables/disables the login button and shows a progress indicator.</summary>
        private void SetBusy(bool busy)
        {
            LoginButton.IsEnabled = !busy;
            LoginButton.Content   = busy ? "Connecting…" : "Connect";
        }

        /// <summary>Shows a WinUI 3 ContentDialog with an error message.</summary>
        private async Task ShowErrorAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title             = "Login Failed",
                Content           = message,
                CloseButtonText   = "OK",
                XamlRoot          = this.XamlRoot   // Required in WinUI 3
            };

            await dialog.ShowAsync();
        }
    }
}
