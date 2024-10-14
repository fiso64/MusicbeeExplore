using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MusicBeePlugin.Models;
using System.Windows.Forms;
using System.Threading;
using HtmlAgilityPack;

namespace MusicBeePlugin.Retrievers
{
    public class LastfmRetriever : IPopularTracksRetriever, ISimilarAlbumRetriever, IAlbumRetriever
    {
        private readonly LastFm _lastFm;

        public LastfmRetriever(Config config)
        {
            _lastFm = new LastFm(config.LastfmApiKey);
        }

        public async Task<List<Track>> GetPopularTracksByArtistAsync(string artist)
        {
            try
            {
                var lastfmTracks = await _lastFm.GetPopularTracksByArtist(artist);
                return lastfmTracks.Select(t => new Track
                {
                    Id = t.Name,
                    Title = t.Name,
                    Artist = artist,
                    Source = Retriever.Lastfm,
                }).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving lastfm data: {ex.Message}");
                return new List<Track>();
            }
        }

        public async Task<List<Track>> GetReleaseTracksAsync(CommentData data)
        {
            try
            {
                var album = await _lastFm.GetAlbum(data.AdditionalData["artist"], data.AdditionalData["title"]);
                if (album == null || album.Tracks == null || album.Tracks.TrackList == null)
                {
                    return new List<Track>();
                }

                return album.Tracks.TrackList.Select((t, i) => new Track
                {
                    Id = t.Name,
                    Title = t.Name,
                    Artist = t.Artist?.Name ?? data.AdditionalData["artist"],
                    Source = Retriever.Lastfm,
                    Length = t.Duration ?? 0,
                    TrackPosition = (i + 1).ToString(),
                }).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving album tracks: {ex.Message}");
                return new List<Track>();
            }
        }

        public async Task<List<Release>> GetSimilarAlbumsAsync(string artist, string title, Action<string> statusChange, CancellationToken ct)
        {
            try
            {
                statusChange("Fetching album information...");
                var album = await _lastFm.GetAlbum(artist, title);
                if (album == null)
                {
                    statusChange("Album not found.");
                    return new List<Release>();
                }

                statusChange("Scraping similar albums...");
                var similarAlbums = await _lastFm.ScrapeSimilarAlbums(album.Url);

                return similarAlbums.Select(a => new Release
                {
                    Id = a.Id,
                    Title = a.Name,
                    Artist = a.Artist,
                    Thumb = a.LargeImageUrl ?? a.Images.FirstOrDefault()?.Url,
                    Source = Retriever.Lastfm,
                    AdditionalData = new Dictionary<string, string>
                    {
                        { "listeners", a.Listeners },
                        { "artist", a.Artist },
                        { "title", a.Name }
                    }
                }).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving similar albums: {ex.Message}");
                return new List<Release>();
            }
        }
    }

    public class LastFm
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;

        public LastFm(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
        }

        public async Task<List<Track>> GetPopularTracksByArtist(string artist)
        {
            string url = $"http://ws.audioscrobbler.com/2.0/?method=artist.gettoptracks&artist={artist}&api_key={_apiKey}&format=json";
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonConvert.DeserializeObject<TopTracksResponse>(response);
            return result.Toptracks.Track;
        }

        public async Task<Album> GetAlbum(string artist, string album)
        {
            string url = $"http://ws.audioscrobbler.com/2.0/?method=album.getinfo&artist={Uri.EscapeDataString(artist)}&album={Uri.EscapeDataString(album)}&api_key={_apiKey}&format=json";
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonConvert.DeserializeObject<AlbumResponse>(response);

            return result?.Album;
        }

        public async Task<List<Album>> ScrapeSimilarAlbums(string albumUrl)
        {
            var response = await _httpClient.GetStringAsync(albumUrl);
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(response);

            var similarAlbums = new List<Album>();
            var albumNodes = doc.DocumentNode.SelectNodes("//ol[contains(@class, 'similar-albums')]//li[contains(@class, 'similar-albums-item-wrap')]");

            if (albumNodes != null)
            {
                foreach (var albumNode in albumNodes)
                {
                    var album = new Album
                    {
                        Name = albumNode.SelectSingleNode(".//h3[contains(@class, 'similar-albums-item-name')]")?.InnerText.Trim(),
                        Artist = albumNode.SelectSingleNode(".//p[contains(@class, 'similar-albums-item-artist')]")?.InnerText.Trim(),
                        Listeners = albumNode.SelectSingleNode(".//p[contains(@class, 'similar-albums-item-listeners')]")?.InnerText.Split()[0],
                        Images = new List<AlbumImage>
                        {
                            new AlbumImage
                            {
                                Url = albumNode.SelectSingleNode(".//img")?.GetAttributeValue("src", null)
                            }
                        }
                    };

                    if (!string.IsNullOrEmpty(album.Name) && !string.IsNullOrEmpty(album.Artist))
                    {
                        similarAlbums.Add(album);
                    }
                }
            }

            return similarAlbums;
        }

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
}
