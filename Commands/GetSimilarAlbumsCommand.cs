using MusicBeePlugin.Models;
using MusicBeePlugin.Retrievers;
using MusicBeePlugin.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.Commands
{
    public class GetSimilarAlbumsCommand : ICommand
    {
        public async Task Execute()
        {
            var (artist, album) = GetQuery();

            if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(album))
            {
                MessageBox.Show("No artist or album selected");
                return;
            }

            try
            {
                var progressWindow = new ProgressWindow("Getting Similar Albums", mbApi.MB_GetWindowHandle());
                progressWindow.Show();

                var lastfmRetriever = new LastfmRetriever(config);
                var similarAlbums = await lastfmRetriever.GetSimilarAlbumsAsync(artist, album, 
                    progressWindow.UpdateStatus, progressWindow.GetCancellationToken());

                if (similarAlbums.Count == 0)
                {
                    MessageBox.Show($"No similar albums found for: {artist} - {album}");
                    progressWindow.Close();
                    return;
                }

                progressWindow.UpdateStatus($"Found {similarAlbums.Count} similar albums");

                string group = $"SimilarTo: {artist} - {album}";

                await CreateDummyAlbums(artist, album, group, similarAlbums, progressWindow);

                Debug.WriteLine($"Done");
                progressWindow.UpdateStatus("Done");
                progressWindow.Close();

                mbApi.MB_RefreshPanels();

                mbApi.MB_OpenFilterInTab(MetaDataType.Comment, ComparisonType.Contains, "<<MBE>>", MetaDataType.Comment, ComparisonType.Contains, group);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Operation cancelled");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error getting similar albums: {ex.Message}");
            }
        }

        private (string artist, string album) GetQuery()
        {
            mbApi.Library_QueryFilesEx("domain=SelectedFiles", out string[] files);

            if (files == null || files.Length == 0)
            {
                return (null, null);
            }

            string artist = mbApi.Library_GetFileTag(files[0], MetaDataType.AlbumArtist);
            string album = mbApi.Library_GetFileTag(files[0], MetaDataType.Album);
            return (artist, album);
        }

        private async Task CreateDummyAlbums(string originalArtist, string originalAlbum, string group, List<Release> releases, ProgressWindow progressWindow)
        {
            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_FOLDER);
            string artistDir = Path.Combine(cachePath, originalArtist.SafeFileName(), $"__{group}".SafeFileName());

            int totalReleases = releases.Count;
            int completedReleases = 0;

            using (var semaphore = new SemaphoreSlim(15))
            {
                var tasks = releases.Select(async release =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        if (progressWindow.GetCancellationToken().IsCancellationRequested)
                        {
                            throw new OperationCanceledException();
                        }

                        progressWindow.UpdateStatus($"Creating dummy for {release.Artist} - {release.Title}");

                        string albumDir = Path.Combine(artistDir, release.Artist.SafeFileName(), release.Title.SafeFileName());
                        Directory.CreateDirectory(albumDir);

                        byte[] coverImage = null;
                        if (!string.IsNullOrEmpty(release.Thumb))
                        {
                            progressWindow.UpdateStatus($"Downloading cover for {release.Artist} - {release.Title}");
                            try
                            {
                                using (var client = new HttpClient())
                                {
                                    coverImage = await client.GetByteArrayAsync(release.Thumb);
                                }
                                File.WriteAllBytes(Path.Combine(albumDir, "cover.jpg"), coverImage);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to download cover for {release.Artist} - {release.Title}: {ex.Message}");
                            }
                        }

                        release.AdditionalData["Group"] = group;

                        var dummyFileInfo = new DummyManager.DummyFileInfo
                        {
                            FilePath = Path.Combine(albumDir, "__Load Album__.opus"),
                            Tags = new Dictionary<MetaDataType, string>
                            {
                                { MetaDataType.TrackTitle, $"[Load Album]" },
                                { MetaDataType.Artist, release.Artist },
                                { MetaDataType.AlbumArtist, release.Artist },
                                { MetaDataType.Album, release.Title },
                                { MetaDataType.Year, "9999" }
                            },
                            CommentData = new CommentData
                            {
                                Id = release.Id,
                                Source = release.Source,
                                State = State.UnloadedAlbum,
                                Role = Role.Main,
                                AdditionalData = release.AdditionalData
                            },
                            Image = coverImage
                        };

                        Plugin.dummyManager.CreateDummyFile(dummyFileInfo);

                        Interlocked.Increment(ref completedReleases);
                        double progressPercentage = (completedReleases / (double)totalReleases) * 100;
                        progressWindow.UpdateProgress(progressPercentage);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }
        }
    }
}
