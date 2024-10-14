using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicBeePlugin.Models;
using MusicBeePlugin.Retrievers;
using Newtonsoft.Json;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.Services
{
    public class DummyProcessor
    {
        public async Task ProcessPlayingTrack()
        {
            string comment = mbApi.NowPlaying_GetFileTag(MetaDataType.Comment);

            if (!comment.Contains(IDENTIFIER))
                return;

            string jsonPart = comment.Replace(IDENTIFIER, string.Empty);
            var data = JsonConvert.DeserializeObject<Models.CommentData>(jsonPart);

            switch (data.State)
            {
                case State.UnloadedAlbum:
                    MusicBeeHelpers.Pause();
                    Debug.WriteLine("Paused track: " + mbApi.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
                    var dummy = DummyFile.FromNowPlaying(mbApi, DummySongFields.Album | DummySongFields.AlbumArtist | DummySongFields.Year | DummySongFields.Image, data);
                    await ProcessUnloadedAlbum(dummy, config.QueueTracksAfterAlbumLoad);
                    break;
                case State.UnloadedTrack:
                    MusicBeeHelpers.Pause();
                    Debug.WriteLine("Paused track: " + mbApi.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
                    dummy = DummyFile.FromNowPlaying(mbApi, DummySongFields.Artist | DummySongFields.Title | DummySongFields.FileUrl, data);
                    await ProcessUnloadedTrack(dummy);
                    break;
                case State.LinkTrack:
                    MusicBeeHelpers.Pause();
                    Debug.WriteLine("Paused track: " + mbApi.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
                    dummy = DummyFile.FromNowPlaying(mbApi, DummySongFields.Artist | DummySongFields.Title | DummySongFields.FileUrl, data);
                    ProcessLinkedTrack(dummy);
                    break;
                default:
                    break;
            }
        }

        public async Task ProcessUnloadedAlbum(DummyFile dummy, bool queueTracks = false)
        {
            if (dummy.AlbumArtist == null || dummy.Album == null || dummy.Year == null)
            {
                throw new ArgumentException("Album artist, album, year cannot be null");
            }

            var retriever = RetrieverRegistry.GetAlbumRetriever(dummy.RetrieverData.Source, config);
            var songs = await retriever.GetReleaseTracksAsync(dummy.RetrieverData);

            string albumFolder = Path.GetDirectoryName(dummy.FileUrl);

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
                var nums = Utils.ParseTrackAndDisc(song.TrackPosition, song.DiscPosition);

                var dummyFileInfo = new DummyManager.DummyFileInfo
                {
                    FilePath = filePath,
                    Tags = new Dictionary<MetaDataType, string>
                    {
                        { MetaDataType.TrackTitle, song.Title },
                        { MetaDataType.Artist, song.Artist },
                        { MetaDataType.AlbumArtist, dummy.AlbumArtist },
                        { MetaDataType.Album, dummy.Album },
                        { MetaDataType.Year, dummy.Year },
                        { MetaDataType.TrackNo, nums.trackNo },
                        { MetaDataType.DiscNo, nums.discNo },
                        { MetaDataType.TrackCount, nums.trackCount },
                        { MetaDataType.DiscCount, nums.discCount }
                    },
                    CommentData = commentData,
                    Image = dummy.Image
                };

                Plugin.dummyManager.CreateDummyFile(dummyFileInfo);

                if (queueTracks)
                {
                    mbApi.NowPlayingList_QueueNext(filePath);
                }
            }

            mbApi.MB_RefreshPanels();

            if (queueTracks)
            {
                int idx = mbApi.NowPlayingList_GetCurrentIndex();
                mbApi.Player_PlayNextTrack();
                mbApi.NowPlayingList_RemoveAt(idx);
            }
        }

        public async Task ProcessUnloadedTrack(DummyFile dummy)
        {
            if (dummy.Artist == null || dummy.Title == null || dummy.FileUrl == null)
            {
                throw new ArgumentException("Artist, title, fileUrl cannot be null.");
            }

            string searchQuery = $"{dummy.Artist} - {dummy.Title}";
            var downloader = new YtDlp();

            if (config.UseMediaPlayer)
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

                Utils.PlayWithMediaPlayer($"{tempFilePath}", config.MediaPlayerCommand);
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

        public void ProcessLinkedTrack(DummyFile dummy)
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
    }
}