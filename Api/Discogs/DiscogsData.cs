using Newtonsoft.Json;
using System.Collections.Generic;

namespace MusicBeePlugin.Api.Discogs
{
    public enum SearchEntityType
    {
        Artist,
        Release,
        Label,
        User
    }

    public class Pagination
    {
        [JsonProperty("per_page")]
        public int PerPage { get; set; }

        [JsonProperty("items")]
        public int Items { get; set; }

        [JsonProperty("page")]
        public int Page { get; set; }

        [JsonProperty("pages")]
        public int Pages { get; set; }

        [JsonProperty("urls")]
        public Dictionary<string, string> Urls { get; set; }
    }

    public class ReleasesResult
    {
        [JsonProperty("pagination")]
        public Pagination Pagination { get; set; }

        [JsonProperty("releases")]
        public List<Release> Releases { get; set; }
    }

    public class Release
    {
        [JsonProperty("artist")]
        public string Artist { get; set; }

        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("main_release")]
        public int? MainRelease { get; set; }

        [JsonProperty("resource_url")]
        public string ResourceUrl { get; set; }

        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("thumb")]
        public string Thumb { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("year")]
        public int? Year { get; set; }

        [JsonProperty("format")]
        public string Format { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }

    public class SearchResult
    {
        [JsonProperty("pagination")]
        public Pagination Pagination { get; set; }

        [JsonProperty("results")]
        public List<SearchEntity> Results { get; set; }
    }

    public class SearchEntity
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("resource_url")]
        public string ResourceUrl { get; set; }

        [JsonProperty("thumb")]
        public string Thumb { get; set; }

        [JsonProperty("type")]
        public SearchEntityType Type { get; set; }
    }

    public class ReleaseDetail
    {
        [JsonProperty("tracklist")]
        public List<Track> Tracklist { get; set; }

        [JsonProperty("videos")]
        public List<Video> Videos { get; set; }

        [JsonProperty("artists")]
        public List<Artist> Artists { get; set; }
    }

    public class Artist
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("resource_url")]
        public string ResourceUrl { get; set; }

        [JsonProperty("role")]
        public string Role { get; set; }
    }

    public class Track
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("duration")]
        public string Duration { get; set; }

        [JsonProperty("position")]
        public string Position { get; set; }

        [JsonProperty("type_")]
        public string Type { get; set; }

        [JsonProperty("artists")]
        public List<Artist> Artists { get; set; }
    }

    public class Video
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("duration")]
        public int Duration { get; set; }

        [JsonProperty("uri")]
        public string Uri { get; set; }
    }
}
