using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MusicBeePlugin.Models;
using System.Windows.Forms;
using System.Threading;

namespace MusicBeePlugin.Retrievers
{
    public class LastfmRetriever : IPopularTracksRetriever, ISimilarAlbumRetriever, IAlbumRetriever
    {
        public class LastfmRetrieverData : RetrieverData
        {
            public string Artist;
            public string Title;
            public LastfmRetrieverData() { Source = Retriever.Lastfm; }
        }

        private readonly Api.Lastfm.Lastfm _lastFm;

        public LastfmRetriever(Config config)
        {
            _lastFm = new Api.Lastfm.Lastfm(config.LastfmApiKey);
        }

        public async Task<List<Track>> GetPopularTracksByArtistAsync(string artist)
        {
            try
            {
                var lastfmTracks = await _lastFm.GetPopularTracksByArtist(artist);
                return lastfmTracks.Select(t => new Track
                {
                    Title = t.Name,
                    Artist = artist,
                    RetrieverData = new RetrieverData { Source = Retriever.Lastfm },
                }).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving lastfm data: {ex.Message}");
                return new List<Track>();
            }
        }

        public async Task<List<Track>> GetReleaseTracksAsync(RetrieverData retrieverData)
        {
            if (!(retrieverData is LastfmRetrieverData data))
            {
                throw new ArgumentException("Data must be of type LastfmRetrieverData.");
            }

            try
            {
                var album = await _lastFm.GetAlbum(data.Artist, data.Title);
                if (album == null || album.Tracks == null || album.Tracks.TrackList == null)
                {
                    return new List<Track>();
                }

                return album.Tracks.TrackList.Select((t, i) => new Track
                {
                    Title = t.Name,
                    Artist = t.Artist?.Name ?? data.Artist,
                    Length = t.Duration ?? 0,
                    TrackPosition = (i + 1).ToString(),
                    RetrieverData = new RetrieverData { Source = Retriever.Lastfm },
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
                    Title = a.Name,
                    Artist = a.Artist,
                    Thumb = a.LargeImageUrl ?? a.Images.FirstOrDefault()?.Url,
                    RetrieverData = new LastfmRetrieverData { Artist = a.Artist, Title = a.Name },
                }).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving similar albums: {ex.Message}");
                return new List<Release>();
            }
        }
    }
}
