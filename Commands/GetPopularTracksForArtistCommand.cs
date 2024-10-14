using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicBeePlugin.Models;
using MusicBeePlugin.Retrievers;
using Newtonsoft.Json;
using static MusicBeePlugin.Plugin;
using MusicBeePlugin.Services;

namespace MusicBeePlugin.Commands
{
    public class GetPopularTracksForArtistCommand : ICommand
    {
        public async Task Execute()
        {
            string artistQuery = GetArtistNameQuery();

            if (string.IsNullOrEmpty(artistQuery))
            {
                MessageBox.Show("No artist name in search field or selection");
                return;
            }

            try
            {
                var progressWindow = new ProgressWindow("Getting Popular Tracks", mbApi.MB_GetWindowHandle());
                progressWindow.Show();

                var lastfmRetriever = new LastfmRetriever(config);
                var tracks = await lastfmRetriever.GetPopularTracksByArtistAsync(artistQuery);

                if (tracks.Count == 0)
                {
                    MessageBox.Show($"No popular tracks found for: {artistQuery}");
                    progressWindow.Close();
                    return;
                }

                progressWindow.UpdateStatus($"Found {tracks.Count} popular tracks");

                await CreateDummyTracks(artistQuery, tracks, progressWindow);

                System.Diagnostics.Debug.WriteLine($"Done");
                progressWindow.UpdateStatus("Done");
                progressWindow.Close();

                mbApi.MB_RefreshPanels();
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("Operation cancelled");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error getting popular tracks: {ex.Message}");
            }
        }

        private string GetArtistNameQuery()
        {
            var hwnd = mbApi.MB_GetWindowHandle();

            string text = "";

            var focus = WinApiHelpers.GetFocus();

            if (WinApiHelpers.GetClassNN(focus).Contains("EDIT"))
            {
                text = WinApiHelpers.GetText(focus);
            }
            else
            {
                mbApi.Library_QueryFilesEx("domain=SelectedFiles", out string[] files);

                if (files != null && files.Length > 0)
                {
                    text = mbApi.Library_GetFileTag(files[0], MetaDataType.Artist);
                }
            }

            System.Diagnostics.Debug.WriteLine($"Query text: {text}");
            return text.Trim();
        }

        private async Task CreateDummyTracks(string artist, List<Track> tracks, ProgressWindow progressWindow)
        {
            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_FOLDER);
            string dummyPath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), DUMMY_FILE);
            string artistDir = Path.Combine(cachePath, artist.SafeFileName(), "Popular Tracks");

            await Plugin.dummyManager.CreateDummyFiles(
                tracks,
                (track, index) =>
                {
                    int popularityRank = index + 1;
                    return new DummyManager.DummyFileInfo
                    {
                        FilePath = Path.Combine(artistDir, $"{popularityRank:D2} - {track.Title}.opus".SafeFileName()),
                        Tags = new Dictionary<MetaDataType, string>
                        {
                            { MetaDataType.TrackTitle, track.Title },
                            { MetaDataType.Artist, artist },
                            { MetaDataType.AlbumArtist, artist },
                            { MetaDataType.Album, "Popular Tracks" },
                            { MetaDataType.Year, "9999" },
                            { MetaDataType.TrackNo, popularityRank.ToString() }
                        },
                        CommentData = new CommentData
                        {
                            Id = track.Id,
                            Source = Retriever.Lastfm,
                            State = State.UnloadedTrack,
                            Role = Role.Main,
                        }
                    };
                },
                progressWindow.UpdateProgress,
                () => progressWindow.GetCancellationToken().IsCancellationRequested
            );
        }
    }
}
