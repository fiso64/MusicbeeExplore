using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace MusicBeePlugin.Api.Lastfm
{
    public class Lastfm
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;

        public Lastfm(string apiKey)
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
                        Name = Utils.UnHtmlString(albumNode.SelectSingleNode(".//h3[contains(@class, 'similar-albums-item-name')]")?.InnerText).Trim(),
                        Artist = Utils.UnHtmlString(albumNode.SelectSingleNode(".//p[contains(@class, 'similar-albums-item-artist')]")?.InnerText).Trim(),
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
    }
}
