using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.RightsManagement;
using System.Text;
using System.Threading.Tasks;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.Models
{
    public enum Retriever
    {
        Discogs,
        MusicBrainz,
        Lastfm,
    }

    public enum ReleaseType
    {
        Album,
        Single,
        EP,
    }

    public class Release
    {
        public string Id;
        public string Title;
        public string Artist;
        public string Album;
        public string Date;
        public string Thumb;
        public bool AppearanceOnly;
        public Retriever Source;

        public Dictionary<string, string> AdditionalData;
    }

    public class Track
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string TrackPosition { get; set; }
        public string DiscPosition { get; set; }
        public int Length { get; set; }
        public Retriever Source;
    }

    public enum State
    {
        UnloadedAlbum,
        UnloadedTrack,
        Loaded,
        LinkTrack
    }

    public enum Role
    {
        Main,
        Appearance,
    }

    public class CommentData
    {
        public string Id;

        [JsonConverter(typeof(StringEnumConverter))]
        public Retriever Source;

        [JsonConverter(typeof(StringEnumConverter))]
        public State State;

        [JsonConverter(typeof(StringEnumConverter))]
        public Role Role;

        public Dictionary<string, string> AdditionalData;

        public static CommentData FromRelease(Release release)
        {
            return new CommentData
            {
                Id = release.Id,
                Source = release.Source,
                State = State.UnloadedAlbum,
                Role = release.AppearanceOnly ? Role.Appearance : Role.Main,
                AdditionalData = release.AdditionalData,
            };
        }

        public static CommentData FromTrack(Track track, CommentData releaseData)
        {
            return new CommentData
            {
                Id = track.Id,
                Source = track.Source,
                State = State.UnloadedTrack,
                Role = releaseData.Role,
            };
        }
    }

    [Flags]
    public enum DummySongFields
    {
        Title = 1,
        Artist = 2,
        AlbumArtist = 4,
        Album = 8,
        Year = 16,
        FileUrl = 32,
        Image = 64,
    }

    public class DummyFile
    {
        public string Title;
        public string Artist;
        public string AlbumArtist;
        public string Album;
        public string Year;
        public string FileUrl;
        public byte[] Image;
        public CommentData RetrieverData;

        private DummyFile() { }

        public static DummyFile FromNowPlaying(MusicBeeApiInterface mbApi, DummySongFields flags, CommentData data = null)
        {
            DummyFile song = new DummyFile();

            song.FileUrl = mbApi.NowPlaying_GetFileUrl();

            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), Plugin.CACHE_FOLDER);

            if (!Utils.IsInDirectory(song.FileUrl, cachePath))
            {
                throw new InvalidOperationException("File is not in cache folder.");
            }

            if (data == null)
            {
                string comment = mbApi.NowPlaying_GetFileTag(MetaDataType.Comment);

                if (!comment.Contains(Plugin.IDENTIFIER))
                    return null;

                string jsonPart = comment.Replace(IDENTIFIER, string.Empty);
                song.RetrieverData = JsonConvert.DeserializeObject<CommentData>(jsonPart); ;
            }
            else
            {
                song.RetrieverData = data;
            }

            if ((flags & DummySongFields.Title) == DummySongFields.Title)
                song.Title = mbApi.NowPlaying_GetFileTag(MetaDataType.TrackTitle);

            if ((flags & DummySongFields.Artist) == DummySongFields.Artist)
                song.Artist = mbApi.NowPlaying_GetFileTag(MetaDataType.Artist);

            if ((flags & DummySongFields.AlbumArtist) == DummySongFields.AlbumArtist)
                song.AlbumArtist = mbApi.NowPlaying_GetFileTag(MetaDataType.AlbumArtist);

            if ((flags & DummySongFields.Album) == DummySongFields.Album)
                song.Album = mbApi.NowPlaying_GetFileTag(MetaDataType.Album);

            if ((flags & DummySongFields.Year) == DummySongFields.Year)
                song.Year = mbApi.NowPlaying_GetFileTag(MetaDataType.Year);

            if ((flags & DummySongFields.Image) == DummySongFields.Image)
            {
                song.FileUrl = song.FileUrl ?? mbApi.NowPlaying_GetFileUrl();
                string coverPath = Path.Combine(Path.GetDirectoryName(song.FileUrl), "cover.jpg");
                if (File.Exists(coverPath))
                    song.Image = File.ReadAllBytes(coverPath);
            }

            return song;
        }

        public static DummyFile FromPath(MusicBeeApiInterface mbApi, string path, DummySongFields flags, CommentData data = null)
        {
            DummyFile song = new DummyFile();

            song.FileUrl = path;

            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), Plugin.CACHE_FOLDER);

            if (!Utils.IsInDirectory(song.FileUrl, cachePath))
            {
                throw new InvalidOperationException("File is not in cache folder.");
            }

            if (data == null)
            {
                string comment = mbApi.Library_GetFileTag(song.FileUrl, MetaDataType.Comment);

                if (!comment.Contains(Plugin.IDENTIFIER))
                    return null;

                string jsonPart = comment.Replace(IDENTIFIER, string.Empty);
                song.RetrieverData = JsonConvert.DeserializeObject<CommentData>(jsonPart); ;
            }
            else
            {
                song.RetrieverData = data;
            }

            if ((flags & DummySongFields.Title) == DummySongFields.Title)
                song.Title = mbApi.Library_GetFileTag(song.FileUrl, MetaDataType.TrackTitle);

            if ((flags & DummySongFields.Artist) == DummySongFields.Artist)
                song.Artist = mbApi.Library_GetFileTag(song.FileUrl, MetaDataType.Artist);

            if ((flags & DummySongFields.AlbumArtist) == DummySongFields.AlbumArtist)
                song.AlbumArtist = mbApi.Library_GetFileTag(song.FileUrl, MetaDataType.AlbumArtist);

            if ((flags & DummySongFields.Album) == DummySongFields.Album)
                song.Album = mbApi.Library_GetFileTag(song.FileUrl, MetaDataType.Album);

            if ((flags & DummySongFields.Year) == DummySongFields.Year)
                song.Year = mbApi.Library_GetFileTag(song.FileUrl, MetaDataType.Year);

            if ((flags & DummySongFields.Image) == DummySongFields.Image)
            {
                string coverPath = Path.Combine(Path.GetDirectoryName(song.FileUrl), "cover.jpg");
                if (File.Exists(coverPath))
                    song.Image = File.ReadAllBytes(coverPath);
            }

            return song;
        }

        public void SetFileTags(MusicBeeApiInterface mbApi)
        {
            if (string.IsNullOrEmpty(FileUrl))
                throw new InvalidOperationException("FileUrl cannot be null.");

            if (!string.IsNullOrEmpty(Title))
                mbApi.Library_SetFileTag(FileUrl, MetaDataType.TrackTitle, Title);

            if (!string.IsNullOrEmpty(Artist))
                mbApi.Library_SetFileTag(FileUrl, MetaDataType.Artist, Artist);

            if (!string.IsNullOrEmpty(AlbumArtist))
                mbApi.Library_SetFileTag(FileUrl, MetaDataType.AlbumArtist, AlbumArtist);

            if (!string.IsNullOrEmpty(Album))
                mbApi.Library_SetFileTag(FileUrl, MetaDataType.Album, Album);

            if (!string.IsNullOrEmpty(Year))
                mbApi.Library_SetFileTag(FileUrl, MetaDataType.Year, Year);

            if (RetrieverData != null)
            {
                string comment = Plugin.IDENTIFIER + JsonConvert.SerializeObject(RetrieverData);
                mbApi.Library_SetFileTag(FileUrl, MetaDataType.Comment, comment);
            }

            if (Image != null)
                mbApi.Library_SetArtworkEx(FileUrl, 0, Image);

            mbApi.Library_CommitTagsToFile(FileUrl);
        }
    }
}
