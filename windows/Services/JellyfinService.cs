using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Streamyfin.Windows.Models;

namespace Streamyfin.Windows.Services
{
    /// <summary>
    /// Service that wraps all Jellyfin REST API calls.
    ///
    /// Design notes
    /// ─────────────
    /// • A single <see cref="HttpClient"/> is shared for the lifetime of the service
    ///   (register as a singleton in your DI container, or use the static factory).
    /// • All public methods are async and return nullable types; they never throw on
    ///   expected 4xx/5xx responses — callers receive <c>null</c> / empty lists.
    /// • The service is self-contained: no static state, no global singletons.
    ///   Wire it into your ViewModel via constructor injection.
    ///
    /// Registering (App.xaml.cs example, using Microsoft.Extensions.DependencyInjection)
    /// ──────────────────────────────────────────────────────────────────────────────────
    ///   services.AddHttpClient&lt;JellyfinService&gt;();
    ///   services.AddSingleton&lt;JellyfinService&gt;();
    ///
    /// Or, if you manage lifetime manually:
    ///   var service = new JellyfinService(new HttpClient());
    /// </summary>
    public sealed class JellyfinService
    {
        // -----------------------------------------------------------------------
        // Constants & static helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Client name sent in the Authorization header so the Jellyfin server
        /// can identify this app in the dashboard.
        /// </summary>
        private const string ClientName    = "Streamyfin/WinUI";
        private const string ClientVersion = "1.0.0";

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // -----------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------

        private readonly HttpClient _http;

        /// <summary>Base URL of the Jellyfin server, e.g. "https://demo.jellyfin.org/stable".</summary>
        public string? ServerUrl     { get; private set; }

        /// <summary>Active access token obtained after a successful login.</summary>
        public string? AccessToken   { get; private set; }

        /// <summary>Authenticated user's ID.</summary>
        public string? UserId        { get; private set; }

        /// <summary>Stable, unique identifier for this Windows device.</summary>
        public string  DeviceId      { get; }

        /// <summary>Human-readable device name shown on the Jellyfin dashboard.</summary>
        public string  DeviceName    { get; }

        // -----------------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------------

        /// <param name="httpClient">
        ///   Injected by DI / IHttpClientFactory. Do NOT set BaseAddress on it
        ///   externally; the service manages the base address itself when the
        ///   server URL is configured.
        /// </param>
        /// <param name="deviceId">
        ///   Optional stable device ID. Falls back to the machine's GUID so it
        ///   survives app restarts.
        /// </param>
        /// <param name="deviceName">Optional friendly device name.</param>
        public JellyfinService(
            HttpClient httpClient,
            string?    deviceId   = null,
            string?    deviceName = null)
        {
            _http      = httpClient;
            DeviceId   = deviceId   ?? GetOrCreateDeviceId();
            DeviceName = deviceName ?? Environment.MachineName;
        }

        // -----------------------------------------------------------------------
        // Configuration
        // -----------------------------------------------------------------------

        /// <summary>
        /// Set (or update) the base URL of the Jellyfin server.
        /// Call this before any other method.
        /// </summary>
        public void Configure(string serverUrl)
        {
            ServerUrl            = serverUrl.TrimEnd('/');
            _http.BaseAddress    = new Uri(ServerUrl + "/");
        }

        // -----------------------------------------------------------------------
        // Authentication
        // -----------------------------------------------------------------------

        /// <summary>
        /// Authenticates the user with a username and password.
        /// On success the service stores the access token and user ID internally.
        ///
        /// Mirrors: POST /Users/AuthenticateByName
        /// Auth header: MediaBrowser Client="…", Device="…", DeviceId="…", Version="…"
        ///   (no Token yet — not authenticated at this point)
        /// </summary>
        public async Task<AuthenticationResult?> LoginAsync(
            string serverUrl,
            string username,
            string password,
            CancellationToken ct = default)
        {
            Configure(serverUrl);

            // The /AuthenticateByName endpoint requires the pre-auth header
            // (without a Token field) so we build it manually here.
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "Users/AuthenticateByName")
            {
                Content = JsonContent.Create(
                    new AuthenticateUserByNameRequest
                    {
                        Username = username,
                        Password = password
                    },
                    options: _jsonOptions)
            };

            request.Headers.Add("X-Emby-Authorization", BuildPreAuthHeader());

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new JellyfinConnectionException(
                    $"Could not reach Jellyfin server at {serverUrl}.", ex);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return null; // Bad credentials — surface as null, not an exception.

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<AuthenticationResult>(_jsonOptions, ct)
                .ConfigureAwait(false);

            if (result is not null)
                ApplySession(result.AccessToken, result.User?.Id);

            return result;
        }

        /// <summary>
        /// Authenticates using an API key (no user context).
        /// The <paramref name="userId"/> must still be supplied because most
        /// library endpoints require it explicitly.
        /// </summary>
        public void LoginWithApiKey(string serverUrl, string apiKey, string userId)
        {
            Configure(serverUrl);
            ApplySession(apiKey, userId);
        }

        /// <summary>Clears the stored token and user ID (logout).</summary>
        public void Logout()
        {
            AccessToken = null;
            UserId      = null;
            _http.DefaultRequestHeaders.Remove("X-Emby-Authorization");
            DeletePersistedSession();
        }

        // -----------------------------------------------------------------------
        // Token persistence
        // -----------------------------------------------------------------------

        /// <summary>
        /// Saves the current session (server URL, user ID, token) to local app
        /// data so it can be restored after the app is restarted.
        /// </summary>
        public void SaveSession()
        {
            if (ServerUrl is null || UserId is null || AccessToken is null) return;

            var payload = new PersistedSession
            {
                ServerUrl   = ServerUrl,
                UserId      = UserId,
                AccessToken = AccessToken
            };

            try
            {
                string path = GetSessionFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path,
                    System.Text.Json.JsonSerializer.Serialize(payload, _jsonOptions));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[JellyfinService] Failed to persist session: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to restore a previously persisted session from disk.
        /// Returns <c>true</c> when a valid session was found and applied;
        /// the caller should verify connectivity before treating the session
        /// as fully active.
        /// </summary>
        public bool TryRestoreSession()
        {
            try
            {
                string path = GetSessionFilePath();
                if (!File.Exists(path)) return false;

                var payload = System.Text.Json.JsonSerializer
                    .Deserialize<PersistedSession>(
                        File.ReadAllText(path), _jsonOptions);

                if (payload is null ||
                    string.IsNullOrWhiteSpace(payload.ServerUrl)  ||
                    string.IsNullOrWhiteSpace(payload.UserId)     ||
                    string.IsNullOrWhiteSpace(payload.AccessToken))
                    return false;

                Configure(payload.ServerUrl);
                ApplySession(payload.AccessToken, payload.UserId);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[JellyfinService] Failed to restore session: {ex.Message}");
                return false;
            }
        }

        /// <summary>Returns true when a token + user ID are loaded in memory.</summary>
        public bool IsAuthenticated => AccessToken is not null && UserId is not null;

        private static string GetSessionFilePath() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Streamyfin",
                "session.json");

        private static void DeletePersistedSession()
        {
            try { File.Delete(GetSessionFilePath()); }
            catch { /* best-effort */ }
        }

        private sealed class PersistedSession
        {
            public string ServerUrl   { get; set; } = string.Empty;
            public string UserId      { get; set; } = string.Empty;
            public string AccessToken { get; set; } = string.Empty;
        }

        // -----------------------------------------------------------------------
        // Session capabilities
        // -----------------------------------------------------------------------

        /// <summary>
        /// Registers this client's playback capabilities with the server.
        /// Should be called after login and again whenever the device profile changes.
        ///
        /// Mirrors: POST /Sessions/Capabilities/Full (session/capabilities.ts)
        /// </summary>
        public async Task PostCapabilitiesAsync(
            DeviceProfile?    deviceProfile = null,
            CancellationToken ct            = default)
        {
            EnsureAuthenticated();

            var capabilities = new ClientCapabilities
            {
                DeviceProfile = deviceProfile ?? BuildDefaultDeviceProfile()
            };

            var response = await _http
                .PostAsJsonAsync("Sessions/Capabilities/Full", capabilities, _jsonOptions, ct)
                .ConfigureAwait(false);

            // 204 No Content is the success response — non-throwing.
            response.EnsureSuccessStatusCode();
        }

        // -----------------------------------------------------------------------
        // User library — individual item lookup
        // -----------------------------------------------------------------------

        /// <summary>
        /// Fetches a single library item by ID for the current user.
        ///
        /// Mirrors: GET /Users/{userId}/Items/{itemId}  (getUserItemData.ts)
        /// </summary>
        public async Task<BaseItemDto?> GetItemAsync(
            string            itemId,
            CancellationToken ct = default)
        {
            EnsureAuthenticated();

            var url = $"Users/{UserId}/Items/{itemId}";
            return await GetJsonAsync<BaseItemDto>(url, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Fetches a single item without a user context.
        ///
        /// Mirrors: GET /Items/{itemId}   (getItemById.ts)
        /// </summary>
        public async Task<BaseItemDto?> GetItemByIdAsync(
            string            itemId,
            CancellationToken ct = default)
        {
            EnsureAuthenticated();

            var url = $"Items/{itemId}";
            return await GetJsonAsync<BaseItemDto>(url, ct).ConfigureAwait(false);
        }

        // -----------------------------------------------------------------------
        // Media Discovery
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns the list of top-level library views (Movies, TV Shows, Music…).
        ///
        /// Mirrors: GET /Users/{userId}/Views
        /// </summary>
        public async Task<List<BaseItemDto>> GetLibraryFoldersAsync(
            CancellationToken ct = default)
        {
            EnsureAuthenticated();

            var result = await GetJsonAsync<BaseItemDtoQueryResult>(
                $"Users/{UserId}/Views", ct).ConfigureAwait(false);

            return result?.Items ?? [];
        }

        /// <summary>
        /// Returns the "Next Up" queue for the current user (all shows, or one
        /// specific series when <paramref name="seriesId"/> is provided).
        ///
        /// Mirrors: GET /Shows/NextUp   (tvshows/nextUp.ts)
        /// </summary>
        public async Task<List<BaseItemDto>> GetNextUpAsync(
            string?           seriesId = null,
            CancellationToken ct       = default)
        {
            EnsureAuthenticated();

            var query = BuildQueryString(new Dictionary<string, string?>
            {
                ["UserId"] = UserId,
                ["Fields"] = "MediaSourceCount,UserData",
                ["SeriesId"] = seriesId
            });

            var result = await GetJsonAsync<BaseItemDtoQueryResult>(
                $"Shows/NextUp{query}", ct).ConfigureAwait(false);

            return result?.Items ?? [];
        }

        /// <summary>
        /// Returns the latest (recently-added) movies for the current user,
        /// optionally scoped to a specific library <paramref name="parentId"/>.
        ///
        /// Mirrors: GET /Users/{userId}/Items/Latest
        /// </summary>
        public async Task<List<BaseItemDto>> GetLatestMoviesAsync(
            string?           parentId = null,
            int               limit    = 16,
            CancellationToken ct       = default)
        {
            EnsureAuthenticated();

            var query = BuildQueryString(new Dictionary<string, string?>
            {
                ["IncludeItemTypes"] = "Movie",
                ["Limit"]            = limit.ToString(),
                ["Fields"]           = "PrimaryImageAspectRatio,BasicSyncInfo,UserData",
                ["ParentId"]         = parentId,
                ["ImageTypeLimit"]   = "1",
                ["EnableImageTypes"] = "Primary,Backdrop,Thumb"
            });

            return await GetJsonAsync<List<BaseItemDto>>(
                $"Users/{UserId}/Items/Latest{query}", ct).ConfigureAwait(false)
                ?? [];
        }

        /// <summary>
        /// Returns items from a library folder (generic paged query).
        /// Supports movies, episodes, music, etc. by passing the appropriate
        /// <paramref name="includeItemTypes"/> filter.
        /// </summary>
        public async Task<BaseItemDtoQueryResult?> GetItemsAsync(
            string?           parentId         = null,
            string?           includeItemTypes = null,
            string?           sortBy           = "SortName",
            string?           sortOrder        = "Ascending",
            int               startIndex       = 0,
            int               limit            = 50,
            string?           fields           = null,
            CancellationToken ct               = default)
        {
            EnsureAuthenticated();

            var query = BuildQueryString(new Dictionary<string, string?>
            {
                ["UserId"]            = UserId,
                ["ParentId"]          = parentId,
                ["IncludeItemTypes"]  = includeItemTypes,
                ["SortBy"]            = sortBy,
                ["SortOrder"]         = sortOrder,
                ["StartIndex"]        = startIndex.ToString(),
                ["Limit"]             = limit.ToString(),
                ["Fields"]            = fields ?? "PrimaryImageAspectRatio,UserData",
                ["Recursive"]         = "true",
                ["ImageTypeLimit"]    = "1",
                ["EnableImageTypes"]  = "Primary,Backdrop,Thumb"
            });

            return await GetJsonAsync<BaseItemDtoQueryResult>(
                $"Users/{UserId}/Items{query}", ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns in-progress (resumable) items for the home screen's
        /// "Continue Watching" row.
        ///
        /// Mirrors: GET /Users/{userId}/Items?Filters=IsResumable
        /// </summary>
        public async Task<List<BaseItemDto>> GetContinueWatchingAsync(
            int               limit = 12,
            CancellationToken ct    = default)
        {
            EnsureAuthenticated();

            var query = BuildQueryString(new Dictionary<string, string?>
            {
                ["Filters"]           = "IsResumable",
                ["Recursive"]         = "true",
                ["IncludeItemTypes"]  = "Movie,Episode",
                ["SortBy"]            = "DatePlayed",
                ["SortOrder"]         = "Descending",
                ["Limit"]             = limit.ToString(),
                ["Fields"]            = "UserData,PrimaryImageAspectRatio",
                ["ImageTypeLimit"]    = "1",
                ["EnableImageTypes"]  = "Primary,Backdrop,Thumb"
            });

            var result = await GetJsonAsync<BaseItemDtoQueryResult>(
                $"Users/{UserId}/Items{query}", ct).ConfigureAwait(false);

            return result?.Items ?? [];
        }

        /// <summary>
        /// Returns box-set collections visible to the current user.
        ///
        /// Mirrors: GET /Users/{userId}/Items?IncludeItemTypes=BoxSet
        /// </summary>
        public async Task<List<BaseItemDto>> GetCollectionsAsync(
            int               limit = 12,
            CancellationToken ct    = default)
        {
            EnsureAuthenticated();

            var query = BuildQueryString(new Dictionary<string, string?>
            {
                ["IncludeItemTypes"]  = "BoxSet",
                ["Recursive"]         = "true",
                ["SortBy"]            = "SortName",
                ["SortOrder"]         = "Ascending",
                ["Limit"]             = limit.ToString(),
                ["Fields"]            = "PrimaryImageAspectRatio,UserData",
                ["ImageTypeLimit"]    = "1",
                ["EnableImageTypes"]  = "Primary,Backdrop"
            });

            var result = await GetJsonAsync<BaseItemDtoQueryResult>(
                $"Users/{UserId}/Items{query}", ct).ConfigureAwait(false);

            return result?.Items ?? [];
        }

        /// <summary>
        /// Returns items similar to the given one (used on detail pages).
        ///
        /// Mirrors: GET /Items/{itemId}/Similar
        /// </summary>
        public async Task<List<BaseItemDto>> GetSimilarItemsAsync(
            string            itemId,
            int               limit = 12,
            CancellationToken ct    = default)
        {
            EnsureAuthenticated();

            var query = BuildQueryString(new Dictionary<string, string?>
            {
                ["UserId"] = UserId,
                ["Limit"]  = limit.ToString(),
                ["Fields"] = "PrimaryImageAspectRatio,UserData"
            });

            var result = await GetJsonAsync<BaseItemDtoQueryResult>(
                $"Items/{itemId}/Similar{query}", ct).ConfigureAwait(false);

            return result?.Items ?? [];
        }

        /// <summary>
        /// Returns the cast and crew for a given item.
        ///
        /// Mirrors: GET /Items/{itemId}/People
        /// </summary>
        public async Task<List<PersonInfo>> GetPeopleAsync(
            string            itemId,
            CancellationToken ct = default)
        {
            EnsureAuthenticated();

            var query = BuildQueryString(new Dictionary<string, string?>
            {
                ["ItemId"] = itemId
            });

            var result = await GetJsonAsync<BaseItemDtoQueryResult>(
                $"Persons{query}", ct).ConfigureAwait(false);

            // Map generic items to the dedicated PersonInfo DTO.
            return result?.Items
                ?.Select(p => new PersonInfo
                {
                    Id   = p.Id,
                    Name = p.Name,
                    Role = p.Overview,       // Jellyfin puts role in Overview for People results
                    Type = p.Type?.ToString()
                })
                .ToList() ?? [];
        }

        // -----------------------------------------------------------------------
        // Playback — stream / transcode URL resolution
        // -----------------------------------------------------------------------

        /// <summary>
        /// Resolves the streaming URL for a media item, applying the transcoding
        /// decision logic ported from <c>media/getStreamUrl.ts</c>.
        ///
        /// The server decides whether the item should be transcoded or direct-played
        /// based on the supplied <paramref name="deviceProfile"/>.  The resulting
        /// <see cref="StreamResult"/> is ready to pass to a media player.
        ///
        /// Mirrors: getStreamUrl() in getStreamUrl.ts
        /// </summary>
        /// <param name="item">The media item to play.</param>
        /// <param name="startTimeTicks">Resume position in 100-ns ticks (0 = from start).</param>
        /// <param name="maxStreamingBitrate">Maximum bitrate in bps. Null = unlimited.</param>
        /// <param name="audioStreamIndex">0-based index of the audio track to use.</param>
        /// <param name="subtitleStreamIndex">Index of the subtitle stream, or -1/null for none.</param>
        /// <param name="mediaSourceId">Force a specific media source (for multi-version items).</param>
        /// <param name="deviceProfile">Device profile to send to the server. Defaults to a
        ///   sensible profile that supports HLS transcoding + direct-play.</param>
        /// <param name="playSessionId">Existing play session ID for resuming a session.</param>
        public async Task<StreamResult?> GetStreamUrlAsync(
            BaseItemDto       item,
            long              startTimeTicks     = 0,
            int?              maxStreamingBitrate = null,
            int               audioStreamIndex    = 0,
            int?              subtitleStreamIndex = null,
            string?           mediaSourceId       = null,
            DeviceProfile?    deviceProfile       = null,
            string?           playSessionId       = null,
            CancellationToken ct                  = default)
        {
            EnsureAuthenticated();

            if (item.Id is null)
                throw new ArgumentException("Item must have a non-null Id.", nameof(item));

            // Live TV programs target the channel rather than the item itself.
            bool isProgram = item.Type == BaseItemKind.Program;
            string targetId = isProgram && item.ChannelId is not null
                ? item.ChannelId
                : item.Id;

            var profile = deviceProfile ?? BuildDefaultDeviceProfile();

            var requestBody = new PlaybackInfoRequest
            {
                UserId              = UserId,
                DeviceProfile       = profile,
                StartTimeTicks      = isProgram ? 0 : startTimeTicks,
                IsPlayback          = true,
                AutoOpenLiveStream  = true,
                MaxStreamingBitrate = maxStreamingBitrate,
                AudioStreamIndex    = audioStreamIndex,
                SubtitleStreamIndex = subtitleStreamIndex,
                MediaSourceId       = mediaSourceId
            };

            var response = await _http.PostAsJsonAsync(
                $"Items/{targetId}/PlaybackInfo", requestBody, _jsonOptions, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[JellyfinService] GetPlaybackInfo failed: {response.StatusCode}");
                return null;
            }

            var playbackInfo = await response.Content
                .ReadFromJsonAsync<PlaybackInfoResponse>(_jsonOptions, ct)
                .ConfigureAwait(false);

            if (playbackInfo?.MediaSources is not { Count: > 0 } sources)
                return null;

            var mediaSource = sources[0];
            string sessionId = playbackInfo.PlaySessionId ?? string.Empty;

            string url = ResolvePlaybackUrl(
                targetId,
                mediaSource,
                new PlaybackUrlParams
                {
                    UserId              = UserId!,
                    StartTimeTicks      = isProgram ? 0 : startTimeTicks,
                    MaxStreamingBitrate = maxStreamingBitrate,
                    AudioStreamIndex    = audioStreamIndex,
                    SubtitleStreamIndex = subtitleStreamIndex,
                    PlaySessionId       = playSessionId ?? sessionId
                });

            return new StreamResult
            {
                Url         = url,
                SessionId   = sessionId,
                MediaSource = mediaSource,
                IsTranscoded = mediaSource.TranscodingUrl is not null
            };
        }

        // -----------------------------------------------------------------------
        // Playback reporting
        // -----------------------------------------------------------------------

        /// <summary>
        /// Reports that playback has started.
        ///
        /// Mirrors: POST /Sessions/Playing
        /// </summary>
        public async Task ReportPlaybackStartAsync(
            PlaybackProgressInfo info,
            CancellationToken    ct = default)
        {
            EnsureAuthenticated();

            var response = await _http
                .PostAsJsonAsync("Sessions/Playing", info, _jsonOptions, ct)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Reports the current playback position so the server can update the
        /// continue-watching queue and sync progress across clients.
        ///
        /// Mirrors: POST /Sessions/Playing/Progress
        /// </summary>
        public async Task ReportPlaybackProgressAsync(
            PlaybackProgressInfo info,
            CancellationToken    ct = default)
        {
            EnsureAuthenticated();

            var response = await _http
                .PostAsJsonAsync("Sessions/Playing/Progress", info, _jsonOptions, ct)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Reports that playback has stopped and optionally marks the item as
        /// played (if the position is near 100%).
        ///
        /// Mirrors: POST /Sessions/Playing/Stopped
        /// </summary>
        public async Task StopPlaybackAsync(
            PlaybackStopInfo  info,
            CancellationToken ct = default)
        {
            EnsureAuthenticated();

            var response = await _http
                .PostAsJsonAsync("Sessions/Playing/Stopped", info, _jsonOptions, ct)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
        }

        // -----------------------------------------------------------------------
        // Image URLs (helper — no HTTP call needed)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Builds the full image URL for a Jellyfin item.
        /// No HTTP call is made; this is purely a URL construction helper.
        /// </summary>
        /// <param name="itemId">The item's Id.</param>
        /// <param name="imageType">e.g. "Primary", "Backdrop", "Thumb"</param>
        /// <param name="tag">The image tag from <see cref="BaseItemDto.ImageTags"/> (for cache busting).</param>
        /// <param name="maxWidth">Optional downscale width.</param>
        public string GetImageUrl(
            string  itemId,
            string  imageType = "Primary",
            string? tag       = null,
            int?    maxWidth  = null)
        {
            EnsureAuthenticated();

            var query = BuildQueryString(new Dictionary<string, string?>
            {
                ["tag"]      = tag,
                ["maxWidth"] = maxWidth?.ToString()
            });

            return $"{ServerUrl}/Items/{itemId}/Images/{imageType}{query}";
        }

        // -----------------------------------------------------------------------
        // Private helpers — URL resolution (ported from getStreamUrl.ts)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Resolves the final playback URL from a media source.
        ///
        /// Decision tree (mirrors getPlaybackUrl() in getStreamUrl.ts):
        ///   1. If the server returned a TranscodingUrl → use it (HLS transcode).
        ///      For -1 subtitle index, swap SubtitleMethod=Encode → Hls.
        ///   2. Otherwise → build a direct-play URL to /Videos/{id}/stream.
        /// </summary>
        private string ResolvePlaybackUrl(
            string           itemId,
            MediaSourceInfo  mediaSource,
            PlaybackUrlParams p)
        {
            string? transcodeUrl = mediaSource.TranscodingUrl;

            // ── Transcoded path ──────────────────────────────────────────────
            if (transcodeUrl is not null)
            {
                // When subtitles are disabled (index -1), prefer HLS delivery
                // so the stream pipeline doesn't hard-encode the subtitle track.
                if (p.SubtitleStreamIndex == -1)
                {
                    transcodeUrl = transcodeUrl.Replace(
                        "SubtitleMethod=Encode",
                        "SubtitleMethod=Hls",
                        StringComparison.OrdinalIgnoreCase);
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[JellyfinService] Transcoded stream: {transcodeUrl}");

                // TranscodingUrl is already a path relative to the server root.
                return $"{ServerUrl}{transcodeUrl}";
            }

            // ── Direct-play path ─────────────────────────────────────────────
            var qs = new StringBuilder();
            AppendParam(qs, "static",               "true");
            AppendParam(qs, "container",             "mp4");
            AppendParam(qs, "mediaSourceId",         mediaSource.Id);
            AppendParam(qs, "subtitleStreamIndex",   p.SubtitleStreamIndex?.ToString());
            AppendParam(qs, "audioStreamIndex",      p.AudioStreamIndex.ToString());
            AppendParam(qs, "deviceId",              DeviceId);
            AppendParam(qs, "api_key",               AccessToken);
            AppendParam(qs, "startTimeTicks",        p.StartTimeTicks.ToString());
            AppendParam(qs, "maxStreamingBitrate",   p.MaxStreamingBitrate?.ToString());
            AppendParam(qs, "userId",                p.UserId);

            if (p.PlaySessionId is not null)
                AppendParam(qs, "playSessionId", p.PlaySessionId);

            string directUrl = $"{ServerUrl}/Videos/{itemId}/stream?{qs}";

            System.Diagnostics.Debug.WriteLine(
                $"[JellyfinService] Direct-play stream: {directUrl}");

            return directUrl;
        }

        // -----------------------------------------------------------------------
        // Private helpers — HTTP
        // -----------------------------------------------------------------------

        private async Task<T?> GetJsonAsync<T>(string relativeUrl, CancellationToken ct)
        {
            try
            {
                return await _http
                    .GetFromJsonAsync<T>(relativeUrl, _jsonOptions, ct)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[JellyfinService] GET {relativeUrl} failed: {ex.Message}");
                return default;
            }
        }

        // -----------------------------------------------------------------------
        // Private helpers — auth / session
        // -----------------------------------------------------------------------

        private void ApplySession(string? accessToken, string? userId)
        {
            AccessToken = accessToken;
            UserId      = userId;

            // Set the Authorization header on every subsequent request.
            _http.DefaultRequestHeaders.Remove("X-Emby-Authorization");
            if (accessToken is not null)
            {
                _http.DefaultRequestHeaders.Add(
                    "X-Emby-Authorization",
                    BuildAuthHeader(accessToken));
            }
        }

        /// <summary>
        /// Builds the pre-auth header (no Token field) for the login request.
        /// e.g.: MediaBrowser Client="Streamyfin/WinUI", Device="DESKTOP-ABC", DeviceId="...", Version="1.0.0"
        /// </summary>
        private string BuildPreAuthHeader() =>
            $"MediaBrowser Client=\"{ClientName}\", " +
            $"Device=\"{DeviceName}\", " +
            $"DeviceId=\"{DeviceId}\", " +
            $"Version=\"{ClientVersion}\"";

        /// <summary>
        /// Builds the post-auth header (with Token) used on all authenticated requests.
        /// Mirrors getAuthHeaders() in jellyfin.ts.
        /// </summary>
        private string BuildAuthHeader(string token) =>
            $"MediaBrowser Client=\"{ClientName}\", " +
            $"Device=\"{DeviceName}\", " +
            $"DeviceId=\"{DeviceId}\", " +
            $"Version=\"{ClientVersion}\", " +
            $"Token=\"{token}\"";

        private void EnsureAuthenticated()
        {
            if (AccessToken is null || UserId is null)
                throw new InvalidOperationException(
                    "JellyfinService is not authenticated. " +
                    "Call LoginAsync() or LoginWithApiKey() first.");
        }

        // -----------------------------------------------------------------------
        // Private helpers — device profile
        // -----------------------------------------------------------------------

        /// <summary>
        /// A sensible default device profile for Windows 11 that
        ///   • direct-plays H.264/H.265/HEVC in MKV, MP4, M4V containers
        ///   • falls back to HLS transcoding for everything else
        ///
        /// This is intentionally conservative; your partner AI's frontend team
        /// can override by passing a custom profile to GetStreamUrlAsync().
        /// </summary>
        private static DeviceProfile BuildDefaultDeviceProfile() => new()
        {
            Name = "Streamyfin Windows",
            MaxStreamingBitrate       = 120_000_000,  // 120 Mbps
            MaxStaticBitrate          = 100_000_000,

            DirectPlayProfiles =
            [
                new() { Type = "Video", Container = "mp4,mkv,m4v",
                         VideoCodec = "h264,h265,hevc,vp9,av1",
                         AudioCodec = "aac,mp3,ac3,eac3,flac,opus" },
                new() { Type = "Audio", Container = "mp3,aac,flac,ogg,opus,m4a" }
            ],

            TranscodingProfiles =
            [
                new()
                {
                    Container          = "ts",
                    Type               = "Video",
                    VideoCodec         = "h264",
                    AudioCodec         = "aac",
                    Protocol           = "hls",
                    Context            = "Streaming",
                    MaxAudioChannels   = "2",
                    MinSegments        = 2,
                    BreakOnNonKeyFrames = true
                }
            ],

            SubtitleProfiles =
            [
                new() { Format = "vtt",  Method = SubtitleDeliveryMethod.Hls },
                new() { Format = "ass",  Method = SubtitleDeliveryMethod.External },
                new() { Format = "ssa",  Method = SubtitleDeliveryMethod.External },
                new() { Format = "srt",  Method = SubtitleDeliveryMethod.External },
                new() { Format = "subrip", Method = SubtitleDeliveryMethod.External },
                new() { Format = "pgs",  Method = SubtitleDeliveryMethod.Drop },
                new() { Format = "pgssub", Method = SubtitleDeliveryMethod.Drop }
            ]
        };

        // -----------------------------------------------------------------------
        // Private helpers — misc utilities
        // -----------------------------------------------------------------------

        private static string BuildQueryString(Dictionary<string, string?> parameters)
        {
            var parts = parameters
                .Where(kvp => kvp.Value is not null)
                .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value!)}");

            string joined = string.Join("&", parts);
            return joined.Length > 0 ? "?" + joined : string.Empty;
        }

        /// <summary>Appends a key=value pair to a <see cref="StringBuilder"/> query string.</summary>
        private static void AppendParam(StringBuilder sb, string key, string? value)
        {
            if (value is null) return;
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
        }

        /// <summary>
        /// Returns a persistent device GUID stored in the user's local app data.
        /// Creates and saves a new one on first run.
        /// </summary>
        private static string GetOrCreateDeviceId()
        {
            const string fileName = "streamyfin_device_id.txt";
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Streamyfin",
                fileName);

            if (File.Exists(path))
            {
                string stored = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(stored))
                    return stored;
            }

            string id = Guid.NewGuid().ToString("N"); // 32 hex chars, no dashes
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, id);
            return id;
        }

        // -----------------------------------------------------------------------
        // Private parameter record (avoids an explosion of method parameters)
        // -----------------------------------------------------------------------

        private sealed class PlaybackUrlParams
        {
            public required string UserId              { get; init; }
            public           long  StartTimeTicks      { get; init; }
            public           int?  MaxStreamingBitrate { get; init; }
            public           int   AudioStreamIndex    { get; init; }
            public           int?  SubtitleStreamIndex { get; init; }
            public           string? PlaySessionId     { get; init; }
        }
    }

    // -----------------------------------------------------------------------
    // Custom exception
    // -----------------------------------------------------------------------

    /// <summary>
    /// Thrown when the HTTP layer cannot reach the Jellyfin server
    /// (network error, bad URL, etc.), as opposed to a 4xx/5xx response
    /// which is surfaced by returning null.
    /// </summary>
    public sealed class JellyfinConnectionException : Exception
    {
        public JellyfinConnectionException(string message, Exception inner)
            : base(message, inner) { }
    }
}
