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
using Newtonsoft.Json;
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
            string artistQuery = GetArtistNameQuery();
            string entityName = null;

            if (string.IsNullOrEmpty(artistQuery))
            {
                MessageBox.Show("No artist name in search field or selection");
                return;
            }

            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), Plugin.CACHE_FOLDER);
            string hiddenCachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), Plugin.CACHE_HIDDEN_FOLDER);

            if (Directory.Exists(hiddenCachePath) && !Directory.Exists(cachePath))
            {
                await Plugin.toggleCachedAlbumsCommand.Execute();

                var libraryQuery = MusicBeeHelpers.ConstructLibraryQuery(
                    (MetaDataType.Comment, ComparisonType.Contains, "<<MBE>>"),
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

                if (releases.Count == 0 && !config.OpenInFilterTab)
                {
                    MessageBox.Show("No additional releases found");
                }
                else
                {
                    await CreateDummyAlbums(entityName, releases, progressWindow);
                }

                Debug.WriteLine($"Done");
                progressWindow.UpdateStatus("Done");
                progressWindow.Close();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Operation cancelled");
            }

            mbApi.MB_RefreshPanels();

            if (Plugin.config.OpenInFilterTab && entityName != null)
                mbApi.MB_OpenFilterInTab(MetaDataType.Comment, ComparisonType.Contains, "<<MBE>>", MetaDataType.AlbumArtist, ComparisonType.Contains, entityName);

            if (config.GetPopularTracks)
            {
                await Plugin.getPopularTracksForArtistCommand.Execute();
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

            Debug.WriteLine($"Query text: {text}");
            return text.Trim();
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

                        var dummyFileInfo = new DummyManager.DummyFileInfo
                        {
                            FilePath = Path.Combine(albumDir, "__Load Album__.opus"),
                            Tags = new Dictionary<MetaDataType, string>
                            {
                                { MetaDataType.TrackTitle, $"[Load Album]{(release.Artist != entityName ? $" / By {release.Artist}" : "")}" },
                                { MetaDataType.Artist, entityName },
                                { MetaDataType.AlbumArtist, string.IsNullOrWhiteSpace(release.Artist) || release.Artist == entityName ? entityName : $"{entityName}; {release.Artist}" },
                                { MetaDataType.Album, release.Title },
                                { MetaDataType.Year, release.Date }
                            },
                            CommentData = Models.CommentData.FromRelease(release),
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
