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

namespace MusicBeePlugin.Retrievers
{
    public class LastfmRetriever
    {
        private readonly LastFm _lastFm;

        public LastfmRetriever(string apiKey)
        {
            _lastFm = new LastFm(apiKey);
        }

        public async Task<List<Track>> GetPopularTracksByArtist(string artist)
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
    }
}
