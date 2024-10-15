using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicBeePlugin.Models;
using MusicBeePlugin.Retrievers;
using static MusicBeePlugin.Plugin;
using MusicBeePlugin.Services;

namespace MusicBeePlugin.Commands
{
    public class GetAlbumsForSelectedArtistCommand : ICommand
    {
        private readonly Retriever source;

        public GetAlbumsForSelectedArtistCommand(Retriever source) 
        {
            this.source = source;
        }

        public async Task Execute()
        {
            string artistQuery = MusicBeeHelpers.GetSearchBoxTextIfFocused() ?? MusicBeeHelpers.GetFirstSelected().artist;

            if (string.IsNullOrEmpty(artistQuery))
            {
                MessageBox.Show("No artist name in search field or selection");
                return;
            }

            string entityName = null;
            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), Plugin.CACHE_FOLDER);
            string hiddenCachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), Plugin.CACHE_HIDDEN_FOLDER);

            if (Directory.Exists(hiddenCachePath) && !Directory.Exists(cachePath))
            {
                await Plugin.toggleCachedAlbumsCommand.Execute();

                var libraryQuery = MusicBeeHelpers.ConstructLibraryQuery(
                    (MetaDataType.Comment, ComparisonType.Contains, Plugin.IDENTIFIER),
                    (MetaDataType.Artist, ComparisonType.Contains, artistQuery)
                );

                if (mbApi.Library_QueryFilesEx(libraryQuery, out string[] files) && files != null && files.Length > 0)
                {
                    return;
                }
            }

            try
            {
                var progressWindow = new ProgressWindow("Getting Albums", mbApi.MB_GetWindowHandle());
                progressWindow.Show();

                var retriever = RetrieverRegistry.GetDiscographyRetriever(source, config);
                List<Release> releases;
                (entityName, releases) = await retriever.GetReleasesAsync(artistQuery, (s) => progressWindow.UpdateStatus(s), progressWindow.GetCancellationToken());

                if (string.IsNullOrEmpty(entityName))
                {
                    MessageBox.Show($"Not found: {artistQuery}");
                    progressWindow.Close();
                    return;
                }

                Debug.WriteLine($"Result count: {releases.Count}");
                progressWindow.UpdateStatus($"Result count: {releases.Count}");

                if (releases.Count == 0)
                {
                    MessageBox.Show($"No releases found for: {artistQuery}");
                    progressWindow.Close();
                    return;
                }

                releases = SkipExisting(releases);

                await CreateDummyAlbums(entityName, releases, progressWindow);

                Debug.WriteLine($"Done");
                progressWindow.UpdateStatus("Done");
                progressWindow.Close();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Operation cancelled");
            }

            if (entityName == null)
                return;

            string group = CommentData.GetGroup(MbeType.MoreAlbums, entityName);
            MusicBeeHelpers.OpenMbeGroup(group, config.OpenInNewTab);

            if (config.GetPopularTracks)
            {
                await Plugin.getPopularTracksForArtistCommand.Execute();
            }

            mbApi.MB_RefreshPanels();
        }

        private List<Models.Release> SkipExisting(List<Models.Release> releases)
        {
            var newReleases = new List<Models.Release>();

            foreach (var release in releases)
            {
                var query = MusicBeeHelpers.ConstructLibraryQuery(
                    (MetaDataType.AlbumArtist, ComparisonType.Contains, release.Artist),
                    (MetaDataType.Album, ComparisonType.Is, release.Title)
                );

                if (mbApi.Library_QueryFilesEx(query, out string[] files) && files != null && files.Length > 0)
                {
                    Debug.WriteLine($"Skipping existing library album: {release.Title}");
                    continue;
                }

                query = MusicBeeHelpers.ConstructLibraryQuery(
                    (MetaDataType.Artist, ComparisonType.Contains, release.Artist),
                    (MetaDataType.Album, ComparisonType.Is, release.Title)
                );

                if (mbApi.Library_QueryFilesEx(query, out files) && files != null && files.Length > 0)
                {
                    Debug.WriteLine($"Skipping existing library album: {release.Title}");
                    continue;
                }

                newReleases.Add(release);
            }

            return newReleases;
        }

        private async Task CreateDummyAlbums(string entityName, List<Models.Release> releases, ProgressWindow progressWindow)
        {
            bool USE_ENTITY_NAME = false;

            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), Plugin.CACHE_FOLDER);

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

                        progressWindow.UpdateStatus($"Creating dummy for {release.Title}");

                        string dir = $"{entityName.SafeFileName()}/{release.Title.SafeFileName()}";
                        string albumDir = Path.Combine(cachePath, dir);
                        Directory.CreateDirectory(albumDir);

                        byte[] coverImage = null;
                        if (!string.IsNullOrEmpty(release.Thumb))
                        {
                            progressWindow.UpdateStatus($"Downloading cover for {release.Title}");
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
                                Debug.WriteLine($"Failed to download cover for {release.Title}: {ex.Message}");
                            }
                        }

                        string trackTitle;
                        string albumArtist;

                        if (USE_ENTITY_NAME)
                        {
                            trackTitle = $"[Load Album]{(release.Artist != entityName && USE_ENTITY_NAME ? $" / By {release.Artist}" : "")}";
                            albumArtist = string.IsNullOrWhiteSpace(release.Artist) || release.Artist == entityName ? entityName : $"{entityName}; {release.Artist}";
                        }
                        else
                        {
                            trackTitle = $"[Load Album]";
                            albumArtist = release.Artist;
                        }

                        var dummyFileInfo = new DummyCreator.DummyFileInfo
                        {
                            FilePath = Path.Combine(albumDir, "__Load Album__.opus"),
                            Tags = new Dictionary<MetaDataType, string>
                            {
                                { MetaDataType.TrackTitle, trackTitle },
                                { MetaDataType.Artist, albumArtist },
                                { MetaDataType.AlbumArtist, albumArtist },
                                { MetaDataType.Album, release.Title },
                                { MetaDataType.Year, release.Date }
                            },
                            CommentData = new CommentData
                            {
                                Type = MbeType.MoreAlbums,
                                State = State.UnloadedAlbum,
                                Group = CommentData.GetGroup(MbeType.MoreAlbums, entityName, release.AppearanceOnly || !release.Artist.ToLower().Contains(entityName.ToLower()) ? MbeSubgroup.Appearance : MbeSubgroup.None),
                                RetrieverData = release.RetrieverData
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

            mbApi.MB_RefreshPanels();
        }
    }
}
