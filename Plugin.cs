using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using MusicBeePlugin.Retrievers;
using MusicBeePlugin.Models;
using Newtonsoft.Json;

// todo: Load selected tracks action
// todo: add menu entries for all actions
// todo: mpv arguments config option
// todo: yt-dlp arguments config option
// todo: find a way to display suggested albums (from discogs, lastfm, maybe bandcamp) for a given album

// todo: fix song skipping bug
//       For some reason the plugin does not receive any notifications while downloading a song,
//       making it impossible to intercept and pause dummy songs

// todo: cancel download when track changes / synchronize access to downloader
// todo: preload next track when current playback is almost done (maybe)
// todo: make track playback faster
// todo: try to reduce false youtube downloads
// todo: bandcamp retrieval and playback

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        public const string IDENTIFIER = "<<MBE>>";
        public const string CACHE_FOLDER = "MusicBeeExplore/cache";
        public const string CACHE_HIDDEN_FOLDER = "MusicBeeExplore/cache-hidden";
        public const string CONFIG_FILE = "MusicBeeExplore/mbe.conf";
        public const string DUMMY_FILE = "MusicBeeExplore/cache/dummy.opus";

        private MusicBeeApiInterface mbApi;
        private PluginInfo about = new PluginInfo();
        private Config config;
        private Config tempConfig = null;
        private string[] mbSettings;
        private Keys searchBoxKey = Keys.None;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            mbApi = new MusicBeeApiInterface();
            mbApi.Initialise(apiInterfacePtr);
            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "MusicBeeExplore";
            about.Description = "Browse MusicBrainz or Discogs in the music explorer view";
            about.Author = "fiso64";
            about.TargetApplication = "";
            about.Type = PluginType.General;
            about.VersionMajor = 1;
            about.VersionMinor = 0;
            about.Revision = 1;
            about.MinInterfaceVersion = MinInterfaceVersion;
            about.MinApiRevision = MinApiRevision;
            about.ReceiveNotifications = (ReceiveNotificationFlags.PlayerEvents);
            about.ConfigurationPanelHeight = 120;

            mbSettings = File.ReadAllLines(Path.Combine(mbApi.Setting_GetPersistentStoragePath(), "MusicBee3Settings.ini"));

            config = new Config(Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CONFIG_FILE));
            config.Load();

            mbApi.MB_RegisterCommand("MusicBeeExplore: Discogs: Query selected or search box artist", async (object sender, EventArgs e) =>
            {
                await GetAlbumsForSelectedArtist(Retriever.Discogs);
            });

            mbApi.MB_RegisterCommand("MusicBeeExplore: MusicBrainz: Query selected or search box artist", async (object sender, EventArgs e) =>
            {
                await GetAlbumsForSelectedArtist(Retriever.MusicBrainz);
            });

            mbApi.MB_RegisterCommand("MusicBeeExplore: Delete cache for selected album artist", async (object sender, EventArgs e) =>
            {
                await DeleteSelectedArtistCache();
            });

            mbApi.MB_RegisterCommand("MusicBeeExplore: Load selected album", async (object sender, EventArgs e) =>
            {
                await LoadSelectedAlbum();
            });

            mbApi.MB_RegisterCommand("MusicBeeExplore: Toggle cached albums", async (object sender, EventArgs e) =>
            {
                await ToggleCachedAlbums();
            });

            mbApi.MB_RegisterCommand("MusicBeeExplore: Last.fm: Get popular tracks for selected artist", async (object sender, EventArgs e) =>
            {
                await GetPopularTracksForArtist();
            });

            return about;
        }

        public async Task GetAlbumsForSelectedArtist(Retriever source)
        {
            string artistQuery = GetArtistNameQuery();

            if (string.IsNullOrEmpty(artistQuery))
            {
                MessageBox.Show("No artist name in search field or selection");
                return;
            }

            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_FOLDER);
            string hiddenCachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_HIDDEN_FOLDER);

            if (Directory.Exists(hiddenCachePath) && !Directory.Exists(cachePath))
            {
                await ToggleCachedAlbums();

                var libraryQuery = ConstructLibraryQuery(
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

                var retriever = RetrieverRegistry.GetRetriever(source, config);
                var (entityName, releases) = await retriever.GetReleasesAsync(artistQuery, (s) => progressWindow.UpdateStatus(s), progressWindow.GetCancellationToken());

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

                if (releases.Count == 0)
                {
                    MessageBox.Show("No additional releases found");
                    progressWindow.Close();
                    return;
                }

                await CreateDummyAlbums(entityName, releases, progressWindow);

                Debug.WriteLine($"Done");
                progressWindow.UpdateStatus("Done");
                progressWindow.Close();

                //SetSearchBoxText(entityName);
                //await Task.Delay(100);
                //WinApiHelpers.SendKey(Keys.Enter);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Operation cancelled");
            }

            mbApi.MB_RefreshPanels();

            if (config.GetPopularTracks)
            {
                await GetPopularTracksForArtist();
            }
        }

        public async Task DeleteSelectedArtistCache()
        {
            string artistQuery = null;
            mbApi.Library_QueryFilesEx("domain=SelectedFiles", out string[] files);
            if (files != null && files.Length > 0)
                artistQuery = mbApi.Library_GetFileTag(files[0], MetaDataType.AlbumArtist);

            if (string.IsNullOrEmpty(artistQuery))
            {
                MessageBox.Show("No artist selected");
                return;
            }

            artistQuery = artistQuery.Split(';')[0];

            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_FOLDER);
            string artistPath = Path.Combine(cachePath, artistQuery.SafeFileName());

            if (Directory.Exists(artistPath))
            {
                try
                {
                    Directory.Delete(artistPath, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error deleting artist cache: {ex.Message}");
                    MessageBox.Show($"Error deleting artist cache: {ex.Message}");
                }
            }

            mbApi.MB_RefreshPanels();
        }

        public async Task LoadSelectedAlbum()
        {
            mbApi.Library_QueryFilesEx("domain=SelectedFiles", out string[] files);

            if (files == null || files.Length == 0)
            {
                MessageBox.Show("No files selected");
                return;
            }

            foreach (var file in files)
            {
                var dummy = DummyFile.FromPath(mbApi, file, DummySongFields.Album | DummySongFields.AlbumArtist | DummySongFields.Year | DummySongFields.Image);

                if (dummy == null || dummy.RetrieverData.State != State.UnloadedAlbum)
                {
                    continue;
                }

                await ProcessUnloadedAlbum(dummy);
            }
        }

        public async Task ToggleCachedAlbums()
        {
            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_FOLDER);
            string hiddenCachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_HIDDEN_FOLDER);

            if (Directory.Exists(cachePath))
            {
                try
                {
                    if (Directory.Exists(hiddenCachePath))
                        Directory.Delete(hiddenCachePath, true);
                    Directory.Move(cachePath, hiddenCachePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error hiding cached albums: {ex.Message}");
                    MessageBox.Show($"Error hiding cached albums: {ex.Message}");
                }
            }
            else if (Directory.Exists(hiddenCachePath))
            {
                try
                {
                    Directory.Move(hiddenCachePath, cachePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error showing cached albums: {ex.Message}");
                    MessageBox.Show($"Error showing cached albums: {ex.Message}");
                }
                
                var files = Directory.GetFiles(cachePath, "*.*", SearchOption.AllDirectories);

                var dirs = Directory.EnumerateDirectories(cachePath, "*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    if (!Utils.IsMusicFile(file))
                        continue;

                    mbApi.Library_AddFileToLibrary(file, LibraryCategory.Music);
                }

                foreach (var dir in dirs)
                {
                    byte[] image = null;

                    if (File.Exists(Path.Combine(dir, "cover.jpg"))) 
                    {
                        image = File.ReadAllBytes(Path.Combine(dir, "cover.jpg"));
                    }

                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        if (!Utils.IsMusicFile(file))
                            continue;

                        mbApi.Library_AddFileToLibrary(file, LibraryCategory.Music);

                        if (image != null)
                            mbApi.Library_SetArtworkEx(file, 0, image);
                    }
                }
            }

            mbApi.MB_RefreshPanels();
        }

        public List<Models.Release> SkipExisting(List<Models.Release> releases)
        {
            var newReleases = new List<Models.Release>();

            foreach (var release in releases)
            {
                var query = ConstructLibraryQuery(
                    (MetaDataType.AlbumArtist, ComparisonType.Contains, release.Artist),
                    (MetaDataType.Album, ComparisonType.Is, release.Title)
                );

                if (mbApi.Library_QueryFilesEx(query, out string[] files) && files != null && files.Length > 0)
                {
                    Debug.WriteLine($"Skipping existing library album: {release.Title}");
                    continue;
                }

                query = ConstructLibraryQuery(
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

        public async Task CreateDummyAlbums(string entityName, List<Models.Release> releases, ProgressWindow progressWindow)
        {
            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_FOLDER);
            string dummyPath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), DUMMY_FILE);
            int totalReleases = releases.Count;
            int completedReleases = 0;

            using (var semaphore = new SemaphoreSlim(15))
            {
                var tasks = releases.Select(async release =>
                {
                    await semaphore.WaitAsync(progressWindow.GetCancellationToken());
                    progressWindow.GetCancellationToken().ThrowIfCancellationRequested();

                    try
                    {
                        string dir = $"{entityName.SafeFileName()}/{release.Title.SafeFileName()}";
                        string albumDir = Path.Combine(cachePath, dir);

                        Directory.CreateDirectory(albumDir);

                        string filePath = Path.Combine(albumDir, "__Load Album__.opus");
                        string coverPath = Path.Combine(albumDir, "cover.jpg");
                        byte[] imageBytes = null;

                        if (!string.IsNullOrEmpty(release.Thumb))
                        {
                            Debug.WriteLine($"Downloading cover: {release.Title}");
                            progressWindow.UpdateStatus($"Downloading cover: {release.Title}");
                            try
                            {
                                using (var httpClient = new HttpClient())
                                {
                                    var request = new HttpRequestMessage(HttpMethod.Get, release.Thumb);
                                    using (var response = await httpClient.SendAsync(request, progressWindow.GetCancellationToken()))
                                    {
                                        response.EnsureSuccessStatusCode();
                                        imageBytes = await response.Content.ReadAsByteArrayAsync();
                                        File.WriteAllBytes(coverPath, imageBytes);
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                Debug.WriteLine("Cover art download canceled");
                                throw;
                            }
                            catch (Exception ex)
                            {
                                progressWindow.UpdateStatus($"Error downloading cover art: {release.Title}");
                                Debug.WriteLine($"Error downloading cover art: {ex.Message}");
                            }
                        }

                        var commentData = Models.CommentData.FromRelease(release);

                        if (!File.Exists(dummyPath))
                        {
                            File.WriteAllBytes(dummyPath, Utils.DummyOpusFile);
                        }

                        File.Copy(dummyPath, filePath, true);

                        mbApi.Library_AddFileToLibrary(filePath, LibraryCategory.Music);

                        string dummyAlbumArtist = entityName;
                        if (!string.IsNullOrWhiteSpace(release.Artist) && release.Artist != entityName)
                            dummyAlbumArtist += $"; {release.Artist}";

                        string dummyTitle = $"[Load Album]{(release.Artist != entityName ? $" / By {release.Artist}" : "")}";

                        mbApi.Library_SetFileTag(filePath, MetaDataType.AlbumArtist, dummyAlbumArtist);
                        mbApi.Library_SetFileTag(filePath, MetaDataType.TrackTitle, dummyTitle);
                        mbApi.Library_SetFileTag(filePath, MetaDataType.Album, release.Title);
                        mbApi.Library_SetFileTag(filePath, MetaDataType.Artist, entityName);
                        mbApi.Library_SetFileTag(filePath, MetaDataType.Year, release.Date);
                        mbApi.Library_SetFileTag(filePath, MetaDataType.Comment, $"{IDENTIFIER}{JsonConvert.SerializeObject(commentData)}");
                        mbApi.Library_CommitTagsToFile(filePath);

                        if (imageBytes != null)
                        {
                            mbApi.Library_SetArtworkEx(filePath, 0, imageBytes);
                        }

                        Debug.WriteLine($"Created album: {release.Title}");
                    }
                    finally
                    {
                        Interlocked.Increment(ref completedReleases);
                        double progressPercentage = (completedReleases / (double)totalReleases) * 100;
                        progressWindow.UpdateProgress(progressPercentage);

                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }
        }

        private async Task ProcessPlayingTrack()
        {
            string comment = mbApi.NowPlaying_GetFileTag(MetaDataType.Comment);

            if (!comment.Contains(IDENTIFIER))
                return;

            string jsonPart = comment.Replace(IDENTIFIER, string.Empty);
            var data = JsonConvert.DeserializeObject<Models.CommentData>(jsonPart);

            switch (data.State)
            {
                case State.UnloadedAlbum:
                    Pause();
                    Debug.WriteLine("Paused track: " +  mbApi.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
                    var dummy = DummyFile.FromNowPlaying(mbApi, DummySongFields.Album | DummySongFields.AlbumArtist | DummySongFields.Year | DummySongFields.Image, data);
                    await ProcessUnloadedAlbum(dummy, config.QueueTracksAfterAlbumLoad);
                    break;
                case State.UnloadedTrack:
                    Pause();
                    Debug.WriteLine("Paused track: " + mbApi.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
                    dummy = DummyFile.FromNowPlaying(mbApi, DummySongFields.Artist | DummySongFields.Title | DummySongFields.FileUrl, data);
                    await ProcessUnloadedTrack(dummy);
                    break;
                case State.LinkTrack:
                    Pause();
                    Debug.WriteLine("Paused track: " + mbApi.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
                    dummy = DummyFile.FromNowPlaying(mbApi, DummySongFields.Artist | DummySongFields.Title | DummySongFields.FileUrl, data);
                    ProcessLinkedTrack(dummy);
                    break;
                default:
                    break;
            }
        }

        private void ProcessLinkedTrack(DummyFile dummy)
        {
            if (dummy.RetrieverData.AdditionalData.TryGetValue("LibraryPath", out string libraryPath))
            {
                if (File.Exists(libraryPath))
                {
                    try
                    {
                        Debug.WriteLine($"Playing linked track: {libraryPath}");
                        int currentIndex = mbApi.NowPlayingList_GetCurrentIndex();
                        mbApi.NowPlayingList_QueueNext(libraryPath);
                        mbApi.Player_PlayNextTrack();
                        mbApi.NowPlayingList_RemoveAt(currentIndex);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error playing linked track: {ex.Message}");
                        MessageBox.Show($"Error playing linked track: {ex.Message}");
                    }
                }
                else
                {
                    MessageBox.Show($"The linked track file no longer exists: {libraryPath}");
                    // Optionally, you could set the state back to UnloadedTrack here
                    // dummy.RetrieverData.State = State.UnloadedTrack;
                    // dummy.SetFileTags(mbApi);
                }
            }
            else
            {
                MessageBox.Show("Error: Linked track path not found in metadata.");
            }
        }

        private async Task ProcessUnloadedAlbum(DummyFile dummy, bool queueTracks = false)
        {
            if (dummy.AlbumArtist == null || dummy.Album == null || dummy.Year == null)
            {
                throw new ArgumentException("Album artist, album, year cannot be null");
            }

            var retriever = RetrieverRegistry.GetRetriever(dummy.RetrieverData.Source, config);
            var songs = await retriever.GetReleaseTracksAsync(dummy.RetrieverData);

            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_FOLDER);
            string dummyPath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), DUMMY_FILE);

            string albumFolder = Path.GetDirectoryName(dummy.FileUrl);

            Directory.CreateDirectory(albumFolder);

            foreach (var song in songs)
            {
                string filePath = Path.Combine(albumFolder, $"{song.TrackPosition} - {song.Artist} - {song.Title}.opus".SafeFileName());

                if (File.Exists(filePath))
                {
                    Debug.WriteLine($"Skipping existing cached track or dummy file: {song.Title}");
                    mbApi.Library_AddFileToLibrary(filePath, LibraryCategory.Music);
                    if (dummy.Image != null)
                    {
                        mbApi.Library_SetArtworkEx(filePath, 0, dummy.Image);
                    }
                    continue;
                }

                var commentData = Models.CommentData.FromTrack(song, dummy.RetrieverData);

                if (!File.Exists(dummyPath))
                {
                    File.WriteAllBytes(dummyPath, Utils.DummyOpusFile);
                }

                File.Copy(dummyPath, filePath, true);

                mbApi.Library_AddFileToLibrary(filePath, LibraryCategory.Music);

                mbApi.Library_SetFileTag(filePath, MetaDataType.TrackTitle, song.Title);
                mbApi.Library_SetFileTag(filePath, MetaDataType.Album, dummy.Album);
                mbApi.Library_SetFileTag(filePath, MetaDataType.Artist, song.Artist);
                mbApi.Library_SetFileTag(filePath, MetaDataType.AlbumArtist, dummy.AlbumArtist);
                mbApi.Library_SetFileTag(filePath, MetaDataType.Year, dummy.Year);
                mbApi.Library_SetFileTag(filePath, MetaDataType.Comment, $"{IDENTIFIER}{JsonConvert.SerializeObject(commentData)}");

                var nums = Utils.ParseTrackAndDisc(song.TrackPosition, song.DiscPosition);
                mbApi.Library_SetFileTag(filePath, MetaDataType.TrackNo, nums.trackNo);
                mbApi.Library_SetFileTag(filePath, MetaDataType.DiscNo, nums.discNo);
                mbApi.Library_SetFileTag(filePath, MetaDataType.TrackCount, nums.trackCount);
                mbApi.Library_SetFileTag(filePath, MetaDataType.DiscCount, nums.discCount);

                mbApi.Library_CommitTagsToFile(filePath);

                mbApi.Library_AddFileToLibrary(filePath, LibraryCategory.Music);
                
                if (dummy.Image != null)
                {
                    mbApi.Library_SetArtworkEx(filePath, 0, dummy.Image);
                }

                Debug.WriteLine($"Created dummy file for: {song.Title}");

                if (queueTracks)
                {
                    mbApi.NowPlayingList_QueueNext(filePath);
                }
            }

            //try
            //{
            //    File.Delete(mbApiInterface.NowPlaying_GetFileUrl());
            //}
            //catch (Exception ex)
            //{
            //    Debug.WriteLine($"Error deleting dummy album file: {ex.Message}");
            //}

            mbApi.MB_RefreshPanels();

            if (queueTracks)
            {
                int idx = mbApi.NowPlayingList_GetCurrentIndex();
                mbApi.Player_PlayNextTrack();
                mbApi.NowPlayingList_RemoveAt(idx);
            }
        }     

        private async Task ProcessUnloadedTrack(DummyFile dummy)
        {
            if (dummy.Artist == null || dummy.Title == null || dummy.FileUrl == null)
            {
                throw new ArgumentException("Artist, title, fileUrl cannot be null.");
            }

            string searchQuery = $"{dummy.Artist} - {dummy.Title}";
            var downloader = new YtDlp();

            if (config.UseMpv)
            {
                int idx = mbApi.NowPlayingList_GetCurrentIndex();
                mbApi.NowPlayingList_QueryFilesEx("", out string[] files);

                string firstFilePath = files[0];
                string tempFilePath = Path.Combine(Path.GetDirectoryName(firstFilePath), "temp_playlist.m3u8");

                List<string> ytdlQueries = new List<string>();

                for (int i = idx; i < files.Length; i++)
                {
                    string filePath = files[i];
                    string title = mbApi.Library_GetFileTag(filePath, MetaDataType.TrackTitle);
                    string artist = mbApi.Library_GetFileTag(filePath, MetaDataType.Artist);
                    string ytdlQuery = $"ytdl://ytsearch:{artist} - {title}";
                    ytdlQueries.Add(ytdlQuery);
                }

                File.WriteAllLines(tempFilePath, ytdlQueries);

                Utils.PlayWithMpv($"{tempFilePath}");
                return;
            }

            string outPathNoExt = Path.Combine(Path.GetDirectoryName(dummy.FileUrl), Path.GetFileNameWithoutExtension(dummy.FileUrl));

            if (File.Exists(outPathNoExt + ".opus"))
            {
                File.Move(outPathNoExt + ".opus", outPathNoExt + ".opus.bak");
            }

            string downloadedPath = null;

            try
            {
                downloadedPath = await downloader.SearchAndDownload(searchQuery, outPathNoExt, showWindow: config.ShowDownloadWindow);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Download canceled.");
                File.Move(outPathNoExt + ".opus.bak", outPathNoExt + ".opus");
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading track {searchQuery}: {ex.Message}");
                MessageBox.Show($"Error downloading track {searchQuery}: {ex.Message}");
                File.Move(outPathNoExt + ".opus.bak", outPathNoExt + ".opus");
                return;
            }

            if (downloadedPath == null)
            {
                Debug.WriteLine($"Download skipped or failed for: {searchQuery}");
                File.Move(outPathNoExt + ".opus.bak", outPathNoExt + ".opus");
                return;
            }

            if (File.Exists(downloadedPath))
            {
                Debug.WriteLine($"Audio download success: {searchQuery}");
                if (File.Exists(outPathNoExt + ".opus.bak"))
                {
                    File.Delete(outPathNoExt + ".opus.bak");
                }
                mbApi.Library_AddFileToLibrary(downloadedPath, LibraryCategory.Music);

                bool stillSame = mbApi.NowPlaying_GetFileUrl() == dummy.FileUrl;

                dummy.FileUrl = downloadedPath;
                dummy.RetrieverData.State = State.Loaded;

                dummy.SetFileTags(mbApi);

                if (stillSame)
                {
                    try
                    {
                        Debug.WriteLine($"Track unchanged, re-queueing downloaded track");
                        mbApi.NowPlayingList_QueueNext(downloadedPath);
                        var idx = mbApi.NowPlayingList_GetCurrentIndex();
                        mbApi.Player_PlayNextTrack();
                        mbApi.NowPlayingList_RemoveAt(idx);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error queuing track after download: {ex.Message}");
                        MessageBox.Show($"Error queuing track after download: {ex.Message}");
                    }
                }
            }
            else
            {
                MessageBox.Show($"Audio download fail: {searchQuery}");
                File.Move(outPathNoExt + ".opus.bak", outPathNoExt + ".opus");
            }
        }

        public string GetArtistNameQuery()
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

            //if (string.IsNullOrWhiteSpace(text))
            //{
            //    text = WinApiHelpers.GetLastNonEmptyEditText(hwnd);
            //}

            Debug.WriteLine($"Query text: {text}");
            return text.Trim();
        }

        public bool Configure(IntPtr panelHandle)
        {
            tempConfig = new Config(config);

            if (panelHandle != IntPtr.Zero)
            {
                Panel configPanel = (Panel)Panel.FromHandle(panelHandle);
                int verticalOffset = 0;

                void addCheckbox(string text, bool initialChecked, Action<bool> onCheckedChanged)
                {   
                    CheckBox checkBox = new CheckBox
                    {
                        Location = new Point(0, verticalOffset),
                        Text = text,
                        AutoSize = true,
                        Checked = initialChecked
                    };
                    checkBox.CheckedChanged += (sender, e) => onCheckedChanged(checkBox.Checked);
                    configPanel.Controls.Add(checkBox);
                    verticalOffset += checkBox.Height;
                }

                void addTextBox(string labelText, string initialText, Action<string> onTextChanged)
                {
                    // Create a panel to hold the label and text box
                    Panel textBoxPanel = new Panel
                    {
                        Location = new Point(0, verticalOffset),
                        Width = configPanel.Width, // Match the width of the parent panel
                        Height = 25 // Set a fixed height for alignment
                    };

                    Label label = new Label
                    {
                        Text = labelText,
                        AutoSize = true,
                        Location = new Point(0, 0) // Align label to the top left
                    };
                    textBoxPanel.Controls.Add(label);

                    TextBox textBox = new TextBox
                    {
                        Location = new Point(label.Width + 10, 0), // Position text box next to the label with padding
                        Width = 200,
                        Height = 15,
                    };
                    textBox.Text = initialText;
                    textBox.TextChanged += (sender, e) => onTextChanged(textBox.Text);
                    textBoxPanel.Controls.Add(textBox);

                    configPanel.Controls.Add(textBoxPanel);
                    verticalOffset += textBoxPanel.Height; // Update vertical offset for the next control
                }

                addCheckbox("Show download window", config.ShowDownloadWindow, (chk) => tempConfig.ShowDownloadWindow = chk);
                addCheckbox("Queue tracks after loading album", config.QueueTracksAfterAlbumLoad, (chk) => tempConfig.QueueTracksAfterAlbumLoad = chk);
                addCheckbox("Use mpv to stream audio", config.UseMpv, (chk) => tempConfig.UseMpv = chk);
                addCheckbox("Get popular tracks when loading albums", config.GetPopularTracks, (chk) => tempConfig.GetPopularTracks = chk);

                // Add the Discogs Token text box
                addTextBox("Discogs Token: ", config.DiscogsToken, (text) => tempConfig.DiscogsToken = text);
                addTextBox("Last.fm API Key: ", config.LastfmApiKey, (text) => tempConfig.LastfmApiKey = text);
            }
            return false;
        }



        public void SaveSettings()
        {
            if (tempConfig != null && !config.Equals(tempConfig))
            {
                config = tempConfig;
                config.Save();
            }
            
            tempConfig = null;
        }

        public void Close(PluginCloseReason reason)
        {

        }

        public void Uninstall()
        {
            string cacheDir = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_FOLDER);
            Directory.Delete(cacheDir, true);
        }

        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            switch (type)
            {
                case NotificationType.TrackChanged:
                    Debug.WriteLine($"Now playing: {mbApi.NowPlaying_GetFileTag(MetaDataType.Artist)} - {mbApi.NowPlaying_GetFileTag(MetaDataType.TrackTitle)}");
                    ProcessPlayingTrack();
                    break;
            }
        }

        public async Task GetPopularTracksForArtist()
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

                var lastfmRetriever = new LastfmRetriever(config.LastfmApiKey);
                var tracks = await lastfmRetriever.GetPopularTracksByArtist(artistQuery);

                if (tracks.Count == 0)
                {
                    MessageBox.Show($"No popular tracks found for: {artistQuery}");
                    progressWindow.Close();
                    return;
                }

                progressWindow.UpdateStatus($"Found {tracks.Count} popular tracks");

                await CreateDummyTracks(artistQuery, tracks, progressWindow);

                Debug.WriteLine($"Done");
                progressWindow.UpdateStatus("Done");
                progressWindow.Close();

                mbApi.MB_RefreshPanels();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Operation cancelled");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error getting popular tracks: {ex.Message}");
            }
        }

        private async Task CreateDummyTracks(string artist, List<Track> tracks, ProgressWindow progressWindow)
        {
            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_FOLDER);
            string dummyPath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), DUMMY_FILE);
            string artistDir = Path.Combine(cachePath, artist.SafeFileName(), "Popular Tracks");

            Directory.CreateDirectory(artistDir);

            int totalTracks = tracks.Count;
            int completedTracks = 0;

            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                int popularityRank = i + 1;

                progressWindow.GetCancellationToken().ThrowIfCancellationRequested();

                string filePath = Path.Combine(artistDir, $"{popularityRank:D2} - {track.Title}.opus".SafeFileName());

                //string libraryTrackPath = FindTrackInLibrary(artist, track.Title);

                var commentData = new CommentData
                {
                    Id = track.Id,
                    Source = Retriever.Lastfm,
                    State = /*libraryTrackPath != null ? State.LinkTrack : */State.UnloadedTrack,
                    Role = Role.Main,
                    AdditionalData = new Dictionary<string, string>()
                };

                //if (libraryTrackPath != null)
                //{
                //    commentData.AdditionalData["LibraryPath"] = libraryTrackPath;
                //}

                if (!File.Exists(dummyPath))
                {
                    File.WriteAllBytes(dummyPath, Utils.DummyOpusFile);
                }

                File.Copy(dummyPath, filePath, true);

                mbApi.Library_AddFileToLibrary(filePath, LibraryCategory.Music);

                mbApi.Library_SetFileTag(filePath, MetaDataType.TrackTitle, track.Title);
                mbApi.Library_SetFileTag(filePath, MetaDataType.Artist, artist);
                mbApi.Library_SetFileTag(filePath, MetaDataType.AlbumArtist, artist);
                mbApi.Library_SetFileTag(filePath, MetaDataType.Album, "Popular Tracks");
                mbApi.Library_SetFileTag(filePath, MetaDataType.Year, "9999");
                mbApi.Library_SetFileTag(filePath, MetaDataType.TrackNo, popularityRank.ToString());
                mbApi.Library_SetFileTag(filePath, MetaDataType.Comment, $"{IDENTIFIER}{JsonConvert.SerializeObject(commentData)}");

                mbApi.Library_CommitTagsToFile(filePath);

                Debug.WriteLine($"Created dummy file for: {track.Title} (Rank: {popularityRank}, State: {commentData.State})");

                completedTracks++;
                double progressPercentage = (completedTracks / (double)totalTracks) * 100;
                progressWindow.UpdateProgress(progressPercentage);
            }
        }

        private string FindTrackInLibrary(string artist, string title)
        {
            var query = ConstructLibraryQuery(
                (MetaDataType.Artist, ComparisonType.Is, artist),
                (MetaDataType.TrackTitle, ComparisonType.Is, title)
            );

            if (mbApi.Library_QueryFilesEx(query, out string[] files) && files != null && files.Length > 0)
            {
                return files[0];
            }

            return null;
        }
    }
}