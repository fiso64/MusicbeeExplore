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
        private readonly int retrieverLevel;

        public GetAlbumsForSelectedArtistCommand(Retriever source, int retrieverLevel = 0) 
        {
            this.source = source;
            this.retrieverLevel = retrieverLevel;
        }

        public async Task Execute()
        {
            string artistQuery = MusicBeeHelpers.GetSearchBoxTextIfFocused() ?? MusicBeeHelpers.GetFirstSelected().artist;

            if (string.IsNullOrWhiteSpace(artistQuery))
            {
                MessageBox.Show("No artist name in search field or selection");
                return;
            }

            EntityRetrieverData artistData = null;
            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), Plugin.CACHE_FOLDER);
            string hiddenCachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), Plugin.CACHE_HIDDEN_FOLDER);
            string group = null;
            bool newResults = false;

            if (Directory.Exists(hiddenCachePath) && !Directory.Exists(cachePath))
            {
                await Plugin.toggleCachedAlbumsCommand.Execute();
            }

            if (cacheRegistry.HasAnyCache(artistQuery, source, MbeType.MoreAlbums, out group))
            {
                CacheRegistry.OpenCacheGroup(group, config.OpenInNewTab);
                return;
            }

            try
            {
                var progressWindow = new ProgressWindow("Getting Albums", mbApi.MB_GetWindowHandle());
                progressWindow.Show();

                var retriever = RetrieverRegistry.GetDiscographyRetriever(source, config);

                artistData = await retriever.GetArtistAsync(artistQuery, (s) => progressWindow.UpdateStatus(s), progressWindow.GetCancellationToken());

                if (artistData == null)
                {
                    MessageBox.Show($"Not found: {artistQuery}");
                    progressWindow.Close();
                    return;
                }

                artistData.RetrieveLevel = Math.Max(retrieverLevel, artistData.RetrieveLevel);

                group = CacheRegistry.GetCacheGroup(MbeType.MoreAlbums, artistData.Name);
                cacheRegistry.Add(artistQuery, source, MbeType.MoreAlbums, group);
                cacheRegistry.Add(artistData.Name, source, MbeType.MoreAlbums, group);
                cacheRegistry.Add(artistData.CacheId, source, MbeType.MoreAlbums, group);

                progressWindow.UpdateTitle($"Getting Albums for {artistData.Name}");

                var releases = await retriever.GetReleasesAsync(artistData, (s) => progressWindow.UpdateStatus(s), progressWindow.GetCancellationToken());

                progressWindow.UpdateStatus($"Result count: {releases.Count}");

                if (releases.Count == 0)
                {
                    MessageBox.Show($"No releases found for: {artistQuery}");
                    progressWindow.Close();
                    return;
                }

                releases = SkipExisting(releases, group);

                if (releases.Count > 0)
                {
                    newResults = true;
                    await CreateDummyAlbums(artistData.Name, releases, progressWindow);
                }

                progressWindow.UpdateStatus("Done");
                progressWindow.Close();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Operation cancelled");
            }

            if (artistData == null)
                return;

            if (newResults || CacheRegistry.CacheGroupHasFiles(group) || config.GetPopularTracks)
            {
                CacheRegistry.OpenCacheGroup(group, config.OpenInNewTab);
            }
            else if (!newResults)
            {
                MessageBox.Show("No additional results found");
            }

            if (config.GetPopularTracks)
            {
                await Plugin.getPopularTracksForArtistCommand.Execute();
            }

            mbApi.MB_RefreshPanels();
        }

        private List<Models.Release> SkipExisting(List<Models.Release> releases, string group)
        {
            var newReleases = new List<Models.Release>();

            bool isSkippable(string p)
            {
                var c = mbApi.Library_GetFileTag(p, MetaDataType.Comment);
                return !c.Contains(IDENTIFIER) || c.Contains(MbeType.MoreAlbums.ToString());
            }

            foreach (var release in releases)
            {
                var query = MusicBeeHelpers.ConstructLibraryQuery(
                    (MetaDataType.AlbumArtist, ComparisonType.Contains, release.Artist),
                    (MetaDataType.Album, ComparisonType.Is, release.Title)
                );

                if (mbApi.Library_QueryFilesEx(query, out string[] files) && files != null && files.Any(x => isSkippable(x)))
                {
                    Debug.WriteLine($"Skipping existing library album: {release.Title}");
                    continue;
                }

                query = MusicBeeHelpers.ConstructLibraryQuery(
                    (MetaDataType.Artist, ComparisonType.Contains, release.Artist),
                    (MetaDataType.Album, ComparisonType.Is, release.Title)
                );

                if (mbApi.Library_QueryFilesEx(query, out files) && files != null && files.Any(x => isSkippable(x)))
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
                                Group = CacheRegistry.GetCacheGroup(MbeType.MoreAlbums, entityName, release.AppearanceOnly || !release.Artist.ToLower().Contains(entityName.ToLower()) ? MbeSubgroup.Appearance : MbeSubgroup.None),
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
