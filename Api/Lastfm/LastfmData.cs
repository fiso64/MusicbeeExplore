using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace MusicBeePlugin.Api.Lastfm
{
    public class TopTracksResponse
    {
        [JsonProperty("toptracks")]
        public TopTracks Toptracks { get; set; }
    }

    public class TopTracks
    {
        [JsonProperty("track")]
        public List<Track> Track { get; set; }
    }

    public class Track
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("playcount")]
        public string Playcount { get; set; }

        [JsonProperty("listeners")]
        public string Listeners { get; set; }
    }

    public class AlbumResponse
    {
        [JsonProperty("album")]
        public Album Album { get; set; }
    }

    public class Album
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("artist")]
        public string Artist { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("mbid")]
        public string Mbid { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("releasedate")]
        public string ReleaseDate { get; set; }

        [JsonProperty("listeners")]
        public string Listeners { get; set; }

        [JsonProperty("playcount")]
        public string Playcount { get; set; }

        [JsonProperty("image")]
        public List<AlbumImage> Images { get; set; }

        [JsonProperty("tracks")]
        public Tracks Tracks { get; set; }

        [JsonProperty("toptags")]
        public TopTags TopTags { get; set; }

        public string LargeImageUrl => Images?.FirstOrDefault(i => i.Size == "large")?.Url;
    }

    public class Tracks
    {
        [JsonProperty("track")]
        public List<AlbumTrack> TrackList { get; set; }
    }

    public class AlbumTrack
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("duration")]
        public int? Duration { get; set; }

        [JsonProperty("mbid")]
        public string Mbid { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("artist")]
        public TrackArtist Artist { get; set; }
    }

    public class TrackArtist
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("mbid")]
        public string Mbid { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
    }

    public class TopTags
    {
        [JsonProperty("tag")]
        public List<Tag> Tags { get; set; }
    }

    public class Tag
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
    }

    public class AlbumImage
    {
        [JsonProperty("size")]
        public string Size { get; set; }

        [JsonProperty("#text")]
        public string Url { get; set; }
    }
}
