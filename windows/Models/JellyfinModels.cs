using System.Text.Json.Serialization;

namespace Streamyfin.Windows.Models
{
    // ---------------------------------------------------------------------------
    // Enumerations
    // ---------------------------------------------------------------------------

    /// <summary>Mirrors BaseItemKind from the Jellyfin TypeScript SDK.</summary>
    public enum BaseItemKind
    {
        Unknown,
        AggregateFolder,
        Audio,
        AudioBook,
        BasePluginFolder,
        Book,
        BoxSet,
        Channel,
        ChannelFolderItem,
        CollectionFolder,
        Episode,
        Folder,
        Genre,
        ManualPlaylistsFolder,
        Movie,
        MusicAlbum,
        MusicArtist,
        MusicGenre,
        MusicVideo,
        Person,
        Photo,
        PhotoAlbum,
        Playlist,
        PlaylistsFolder,
        Program,
        Recording,
        Season,
        Series,
        Studio,
        Trailer,
        TvChannel,
        TvProgram,
        UserRootFolder,
        UserView,
        Video,
        Year
    }

    public enum MediaStreamType
    {
        Unknown,
        Audio,
        Video,
        Subtitle,
        EmbeddedImage,
        Data,
        Lyric
    }

    public enum SubtitleDeliveryMethod
    {
        Encode,
        Embed,
        External,
        Hls,
        Drop
    }

    public enum VideoType
    {
        VideoFile,
        Iso,
        Dvd,
        BluRay
    }

    // ---------------------------------------------------------------------------
    // Authentication
    // ---------------------------------------------------------------------------

    /// <summary>Request body for username+password authentication.</summary>
    public class AuthenticateUserByNameRequest
    {
        [JsonPropertyName("Username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("Pw")]
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>Response payload from /Users/AuthenticateByName.</summary>
    public class AuthenticationResult
    {
        [JsonPropertyName("User")]
        public UserDto? User { get; set; }

        [JsonPropertyName("SessionInfo")]
        public SessionInfo? SessionInfo { get; set; }

        [JsonPropertyName("AccessToken")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("ServerId")]
        public string? ServerId { get; set; }
    }

    // ---------------------------------------------------------------------------
    // User
    // ---------------------------------------------------------------------------

    public class UserDto
    {
        [JsonPropertyName("Id")]
        public string? Id { get; set; }

        [JsonPropertyName("Name")]
        public string? Name { get; set; }

        [JsonPropertyName("ServerId")]
        public string? ServerId { get; set; }

        [JsonPropertyName("HasPassword")]
        public bool HasPassword { get; set; }

        [JsonPropertyName("HasConfiguredPassword")]
        public bool HasConfiguredPassword { get; set; }

        [JsonPropertyName("Configuration")]
        public UserConfiguration? Configuration { get; set; }

        [JsonPropertyName("Policy")]
        public UserPolicy? Policy { get; set; }
    }

    public class UserConfiguration
    {
        [JsonPropertyName("AudioLanguagePreference")]
        public string? AudioLanguagePreference { get; set; }

        [JsonPropertyName("PlayDefaultAudioTrack")]
        public bool PlayDefaultAudioTrack { get; set; }

        [JsonPropertyName("SubtitleLanguagePreference")]
        public string? SubtitleLanguagePreference { get; set; }

        [JsonPropertyName("DisplayMissingEpisodes")]
        public bool DisplayMissingEpisodes { get; set; }
    }

    public class UserPolicy
    {
        [JsonPropertyName("IsAdministrator")]
        public bool IsAdministrator { get; set; }

        [JsonPropertyName("IsDisabled")]
        public bool IsDisabled { get; set; }

        [JsonPropertyName("EnableMediaPlayback")]
        public bool EnableMediaPlayback { get; set; }

        [JsonPropertyName("EnableVideoPlaybackTranscoding")]
        public bool EnableVideoPlaybackTranscoding { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Sessions
    // ---------------------------------------------------------------------------

    public class SessionInfo
    {
        [JsonPropertyName("Id")]
        public string? Id { get; set; }

        [JsonPropertyName("DeviceId")]
        public string? DeviceId { get; set; }

        [JsonPropertyName("DeviceName")]
        public string? DeviceName { get; set; }

        [JsonPropertyName("Client")]
        public string? Client { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Library / Items
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Core item DTO — mirrors BaseItemDto from @jellyfin/sdk.
    /// Only the fields actually consumed by this client are included;
    /// additional fields are silently ignored by System.Text.Json.
    /// </summary>
    public class BaseItemDto
    {
        [JsonPropertyName("Id")]
        public string? Id { get; set; }

        [JsonPropertyName("Name")]
        public string? Name { get; set; }

        [JsonPropertyName("ServerId")]
        public string? ServerId { get; set; }

        [JsonPropertyName("Type")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BaseItemKind? Type { get; set; }

        [JsonPropertyName("Overview")]
        public string? Overview { get; set; }

        [JsonPropertyName("ProductionYear")]
        public int? ProductionYear { get; set; }

        [JsonPropertyName("PremiereDate")]
        public DateTimeOffset? PremiereDate { get; set; }

        [JsonPropertyName("OfficialRating")]
        public string? OfficialRating { get; set; }

        [JsonPropertyName("CommunityRating")]
        public float? CommunityRating { get; set; }

        [JsonPropertyName("RunTimeTicks")]
        public long? RunTimeTicks { get; set; }

        // Episode-specific
        [JsonPropertyName("IndexNumber")]
        public int? IndexNumber { get; set; }

        [JsonPropertyName("ParentIndexNumber")]
        public int? ParentIndexNumber { get; set; }

        [JsonPropertyName("SeriesId")]
        public string? SeriesId { get; set; }

        [JsonPropertyName("SeriesName")]
        public string? SeriesName { get; set; }

        [JsonPropertyName("SeasonId")]
        public string? SeasonId { get; set; }

        // Live TV
        [JsonPropertyName("ChannelId")]
        public string? ChannelId { get; set; }

        // Images
        [JsonPropertyName("ImageTags")]
        public Dictionary<string, string>? ImageTags { get; set; }

        [JsonPropertyName("BackdropImageTags")]
        public List<string>? BackdropImageTags { get; set; }

        // Media info
        [JsonPropertyName("MediaSources")]
        public List<MediaSourceInfo>? MediaSources { get; set; }

        [JsonPropertyName("MediaStreams")]
        public List<MediaStream>? MediaStreams { get; set; }

        // User data (watch state)
        [JsonPropertyName("UserData")]
        public UserItemDataDto? UserData { get; set; }
    }

    /// <summary>Paginated list wrapper returned by most /Items endpoints.</summary>
    public class BaseItemDtoQueryResult
    {
        [JsonPropertyName("Items")]
        public List<BaseItemDto>? Items { get; set; }

        [JsonPropertyName("TotalRecordCount")]
        public int TotalRecordCount { get; set; }

        [JsonPropertyName("StartIndex")]
        public int StartIndex { get; set; }
    }

    // ---------------------------------------------------------------------------
    // User Item Data (watch state, ratings)
    // ---------------------------------------------------------------------------

    public class UserItemDataDto
    {
        [JsonPropertyName("PlaybackPositionTicks")]
        public long? PlaybackPositionTicks { get; set; }

        [JsonPropertyName("PlayCount")]
        public int PlayCount { get; set; }

        [JsonPropertyName("IsFavorite")]
        public bool IsFavorite { get; set; }

        [JsonPropertyName("Played")]
        public bool Played { get; set; }

        [JsonPropertyName("Key")]
        public string? Key { get; set; }

        [JsonPropertyName("ItemId")]
        public string? ItemId { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Media Sources & Streams
    // ---------------------------------------------------------------------------

    public class MediaSourceInfo
    {
        [JsonPropertyName("Id")]
        public string? Id { get; set; }

        [JsonPropertyName("Path")]
        public string? Path { get; set; }

        [JsonPropertyName("Protocol")]
        public string? Protocol { get; set; }

        [JsonPropertyName("ETag")]
        public string? ETag { get; set; }

        [JsonPropertyName("Size")]
        public long? Size { get; set; }

        [JsonPropertyName("Bitrate")]
        public int? Bitrate { get; set; }

        [JsonPropertyName("Container")]
        public string? Container { get; set; }

        [JsonPropertyName("SupportsTranscoding")]
        public bool SupportsTranscoding { get; set; }

        [JsonPropertyName("SupportsDirectStream")]
        public bool SupportsDirectStream { get; set; }

        [JsonPropertyName("SupportsDirectPlay")]
        public bool SupportsDirectPlay { get; set; }

        [JsonPropertyName("TranscodingUrl")]
        public string? TranscodingUrl { get; set; }

        [JsonPropertyName("TranscodingSubProtocol")]
        public string? TranscodingSubProtocol { get; set; }

        [JsonPropertyName("TranscodingContainer")]
        public string? TranscodingContainer { get; set; }

        [JsonPropertyName("DefaultAudioStreamIndex")]
        public int? DefaultAudioStreamIndex { get; set; }

        [JsonPropertyName("DefaultSubtitleStreamIndex")]
        public int? DefaultSubtitleStreamIndex { get; set; }

        [JsonPropertyName("MediaStreams")]
        public List<MediaStream>? MediaStreams { get; set; }

        [JsonPropertyName("VideoType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public VideoType? VideoType { get; set; }
    }

    public class MediaStream
    {
        [JsonPropertyName("Index")]
        public int Index { get; set; }

        [JsonPropertyName("Type")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MediaStreamType Type { get; set; }

        [JsonPropertyName("Codec")]
        public string? Codec { get; set; }

        [JsonPropertyName("Language")]
        public string? Language { get; set; }

        [JsonPropertyName("DisplayTitle")]
        public string? DisplayTitle { get; set; }

        [JsonPropertyName("IsDefault")]
        public bool IsDefault { get; set; }

        [JsonPropertyName("IsForced")]
        public bool IsForced { get; set; }

        [JsonPropertyName("IsExternal")]
        public bool IsExternal { get; set; }

        [JsonPropertyName("BitRate")]
        public int? BitRate { get; set; }

        [JsonPropertyName("Channels")]
        public int? Channels { get; set; }

        [JsonPropertyName("SampleRate")]
        public int? SampleRate { get; set; }

        // Video-specific
        [JsonPropertyName("Width")]
        public int? Width { get; set; }

        [JsonPropertyName("Height")]
        public int? Height { get; set; }

        [JsonPropertyName("VideoRange")]
        public string? VideoRange { get; set; }

        [JsonPropertyName("ColorSpace")]
        public string? ColorSpace { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Playback Info
    // ---------------------------------------------------------------------------

    /// <summary>Request body for POST /Items/{itemId}/PlaybackInfo.</summary>
    public class PlaybackInfoRequest
    {
        [JsonPropertyName("UserId")]
        public string? UserId { get; set; }

        [JsonPropertyName("MaxStreamingBitrate")]
        public int? MaxStreamingBitrate { get; set; }

        [JsonPropertyName("StartTimeTicks")]
        public long? StartTimeTicks { get; set; }

        [JsonPropertyName("AudioStreamIndex")]
        public int? AudioStreamIndex { get; set; }

        [JsonPropertyName("SubtitleStreamIndex")]
        public int? SubtitleStreamIndex { get; set; }

        [JsonPropertyName("MediaSourceId")]
        public string? MediaSourceId { get; set; }

        [JsonPropertyName("IsPlayback")]
        public bool IsPlayback { get; set; } = true;

        [JsonPropertyName("AutoOpenLiveStream")]
        public bool AutoOpenLiveStream { get; set; } = true;

        [JsonPropertyName("DeviceProfile")]
        public DeviceProfile? DeviceProfile { get; set; }
    }

    /// <summary>Response from GET or POST /Items/{itemId}/PlaybackInfo.</summary>
    public class PlaybackInfoResponse
    {
        [JsonPropertyName("MediaSources")]
        public List<MediaSourceInfo>? MediaSources { get; set; }

        [JsonPropertyName("PlaySessionId")]
        public string? PlaySessionId { get; set; }

        [JsonPropertyName("ErrorCode")]
        public string? ErrorCode { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Device Profile (used when requesting transcoding decisions)
    // ---------------------------------------------------------------------------

    public class DeviceProfile
    {
        [JsonPropertyName("Name")]
        public string? Name { get; set; }

        [JsonPropertyName("MaxStreamingBitrate")]
        public int? MaxStreamingBitrate { get; set; }

        [JsonPropertyName("MaxStaticBitrate")]
        public int? MaxStaticBitrate { get; set; }

        [JsonPropertyName("MusicStreamingTranscodingBitrate")]
        public int? MusicStreamingTranscodingBitrate { get; set; }

        [JsonPropertyName("DirectPlayProfiles")]
        public List<DirectPlayProfile>? DirectPlayProfiles { get; set; }

        [JsonPropertyName("TranscodingProfiles")]
        public List<TranscodingProfile>? TranscodingProfiles { get; set; }

        [JsonPropertyName("ContainerProfiles")]
        public List<ContainerProfile>? ContainerProfiles { get; set; }

        [JsonPropertyName("CodecProfiles")]
        public List<CodecProfile>? CodecProfiles { get; set; }

        [JsonPropertyName("SubtitleProfiles")]
        public List<SubtitleProfile>? SubtitleProfiles { get; set; }
    }

    public class DirectPlayProfile
    {
        [JsonPropertyName("Type")]
        public string? Type { get; set; }   // "Video" | "Audio"

        [JsonPropertyName("Container")]
        public string? Container { get; set; }

        [JsonPropertyName("VideoCodec")]
        public string? VideoCodec { get; set; }

        [JsonPropertyName("AudioCodec")]
        public string? AudioCodec { get; set; }
    }

    public class TranscodingProfile
    {
        [JsonPropertyName("Container")]
        public string? Container { get; set; }

        [JsonPropertyName("Type")]
        public string? Type { get; set; }

        [JsonPropertyName("VideoCodec")]
        public string? VideoCodec { get; set; }

        [JsonPropertyName("AudioCodec")]
        public string? AudioCodec { get; set; }

        [JsonPropertyName("Protocol")]
        public string? Protocol { get; set; }

        [JsonPropertyName("Context")]
        public string? Context { get; set; }

        [JsonPropertyName("MaxAudioChannels")]
        public string? MaxAudioChannels { get; set; }

        [JsonPropertyName("MinSegments")]
        public int MinSegments { get; set; }

        [JsonPropertyName("BreakOnNonKeyFrames")]
        public bool BreakOnNonKeyFrames { get; set; }
    }

    public class ContainerProfile
    {
        [JsonPropertyName("Type")]
        public string? Type { get; set; }

        [JsonPropertyName("Container")]
        public string? Container { get; set; }

        [JsonPropertyName("Conditions")]
        public List<ProfileCondition>? Conditions { get; set; }
    }

    public class CodecProfile
    {
        [JsonPropertyName("Type")]
        public string? Type { get; set; }

        [JsonPropertyName("Codec")]
        public string? Codec { get; set; }

        [JsonPropertyName("Conditions")]
        public List<ProfileCondition>? Conditions { get; set; }
    }

    public class ProfileCondition
    {
        [JsonPropertyName("Condition")]
        public string? Condition { get; set; }

        [JsonPropertyName("Property")]
        public string? Property { get; set; }

        [JsonPropertyName("Value")]
        public string? Value { get; set; }

        [JsonPropertyName("IsRequired")]
        public bool IsRequired { get; set; }
    }

    public class SubtitleProfile
    {
        [JsonPropertyName("Format")]
        public string? Format { get; set; }

        [JsonPropertyName("Method")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SubtitleDeliveryMethod Method { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Playback Reporting / Progress
    // ---------------------------------------------------------------------------

    /// <summary>Body sent to POST /Sessions/Playing/Progress.</summary>
    public class PlaybackProgressInfo
    {
        [JsonPropertyName("ItemId")]
        public string? ItemId { get; set; }

        [JsonPropertyName("SessionId")]
        public string? SessionId { get; set; }

        [JsonPropertyName("MediaSourceId")]
        public string? MediaSourceId { get; set; }

        [JsonPropertyName("PositionTicks")]
        public long? PositionTicks { get; set; }

        [JsonPropertyName("IsPaused")]
        public bool IsPaused { get; set; }

        [JsonPropertyName("IsMuted")]
        public bool IsMuted { get; set; }

        [JsonPropertyName("AudioStreamIndex")]
        public int? AudioStreamIndex { get; set; }

        [JsonPropertyName("SubtitleStreamIndex")]
        public int? SubtitleStreamIndex { get; set; }

        [JsonPropertyName("VolumeLevel")]
        public int? VolumeLevel { get; set; }

        [JsonPropertyName("PlayMethod")]
        public string? PlayMethod { get; set; }   // "Transcode" | "DirectStream" | "DirectPlay"

        [JsonPropertyName("PlaySessionId")]
        public string? PlaySessionId { get; set; }

        [JsonPropertyName("CanSeek")]
        public bool CanSeek { get; set; } = true;
    }

    /// <summary>Body sent to POST /Sessions/Playing/Stopped.</summary>
    public class PlaybackStopInfo
    {
        [JsonPropertyName("ItemId")]
        public string? ItemId { get; set; }

        [JsonPropertyName("SessionId")]
        public string? SessionId { get; set; }

        [JsonPropertyName("MediaSourceId")]
        public string? MediaSourceId { get; set; }

        [JsonPropertyName("PositionTicks")]
        public long? PositionTicks { get; set; }

        [JsonPropertyName("PlaySessionId")]
        public string? PlaySessionId { get; set; }

        [JsonPropertyName("Failed")]
        public bool Failed { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Stream resolution result (internal; not a Jellyfin DTO)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returned by <see cref="Services.JellyfinService.GetStreamUrlAsync"/>.
    /// Bundles every piece of information a player needs to start playback.
    /// </summary>
    public class StreamResult
    {
        /// <summary>Fully-qualified URL ready to hand to the media player.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>The Jellyfin play session id (needed for progress reporting).</summary>
        public string? SessionId { get; set; }

        /// <summary>The resolved media source (audio/subtitle track info, etc.).</summary>
        public MediaSourceInfo? MediaSource { get; set; }

        /// <summary>True when the server chose to transcode rather than direct-play.</summary>
        public bool IsTranscoded { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Session Capabilities
    // ---------------------------------------------------------------------------

    public class ClientCapabilities
    {
        [JsonPropertyName("PlayableMediaTypes")]
        public List<string> PlayableMediaTypes { get; set; } = ["Audio", "Video"];

        [JsonPropertyName("SupportedCommands")]
        public List<string> SupportedCommands { get; set; } =
        [
            "PlayState", "Play", "ToggleFullscreen",
            "DisplayMessage", "Mute", "Unmute",
            "SetVolume", "ToggleMute"
        ];

        [JsonPropertyName("SupportsMediaControl")]
        public bool SupportsMediaControl { get; set; } = true;

        [JsonPropertyName("DeviceProfile")]
        public DeviceProfile? DeviceProfile { get; set; }
    }

    // ---------------------------------------------------------------------------
    // People
    // ---------------------------------------------------------------------------

    /// <summary>Lightweight person record returned by GetPeopleAsync.</summary>
    public class PersonInfo
    {
        public string? Id   { get; set; }
        public string? Name { get; set; }
        /// <summary>Role / character name (e.g. "Director", "Actor").</summary>
        public string? Role { get; set; }
        /// <summary>Person type string, e.g. "Actor", "Director", "Writer".</summary>
        public string? Type { get; set; }
    }

    // ---------------------------------------------------------------------------
    // Playback navigation parameter
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Passed as the navigation parameter from ItemDetailPage to MediaPlayerPage.
    /// Contains everything the player needs to resolve and start playback.
    /// </summary>
    public class PlaybackParameter
    {
        /// <summary>Jellyfin item Id.</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Display title shown in the player overlay.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Position to seek to on load (in 100-ns ticks).
        /// 0 = play from the beginning.
        /// </summary>
        public long StartTimeTicks { get; set; }

        /// <summary>The media source Id to force (for multi-version items). Null = auto.</summary>
        public string? MediaSourceId { get; set; }
    }
}
