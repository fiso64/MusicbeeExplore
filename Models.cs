using MusicBeePlugin.Retrievers;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.Models
{
    public enum Retriever
    {
        Discogs,
        MusicBrainz,
        Lastfm,
    }

    public enum State
    {
        UnloadedAlbum,
        UnloadedTrack,
        Loaded,
        LinkTrack
    }

    public enum MbeType
    {
        MoreAlbums,
        PopularTracks,
        SimilarAlbums,
    }

    public enum MbeSubgroup
    {
        None,
        Main,
        Appearance,
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

    public class RetrieverData
    {
        public Retriever Source;
    }

    public class EntityRetrieverData : RetrieverData
    {
        public string Name;
        public string CacheId;
        public int RetrieveLevel;
    }

    public class Release
    {
        public string Title;
        public string Artist;
        public string Album;
        public string Date;
        public string Thumb;
        public bool AppearanceOnly;

        public RetrieverData RetrieverData;
    }

    public class Track
    {
        public string Title;
        public string Artist;
        public string TrackPosition;
        public string DiscPosition;
        public int Length;

        public RetrieverData RetrieverData;
    }

    [JsonConverter(typeof(CommentDataConverter))]
    public class CommentData
    {
        [JsonConverter(typeof(StringEnumConverter)), JsonProperty(Plugin.IDENTIFIER)]
        public MbeType Type;

        [JsonConverter(typeof(StringEnumConverter))]
        public State State;

        public RetrieverData RetrieverData;

        public string Group;

        public static CommentData FromTrack(Track track, CommentData releaseData)
        {
            var data = new CommentData
            {
                Type = releaseData.Type,
                State = State.UnloadedTrack,
                Group = releaseData.Group,
                RetrieverData = track.RetrieverData,
            };
            return data;
        }
    }

    public class CommentDataConverter : JsonConverter<CommentData>
    {
        public override void WriteJson(JsonWriter writer, CommentData value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override CommentData ReadJson(JsonReader reader, Type objectType, CommentData existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);

            CommentData commentData = new CommentData();
            commentData.Type = jo[Plugin.IDENTIFIER].ToObject<MbeType>(serializer);
            commentData.State = jo[nameof(commentData.State)].ToObject<State>(serializer);
            commentData.Group = jo[nameof(commentData.Group)].ToObject<string>(serializer);

            var retrieverDataJObject = jo[nameof(commentData.RetrieverData)] as JObject;
            if (retrieverDataJObject != null && retrieverDataJObject.ContainsKey("Source"))
            {
                Retriever source = retrieverDataJObject["Source"].ToObject<Retriever>(serializer);

                switch (source)
                {
                    case Retriever.Discogs:
                        commentData.RetrieverData = retrieverDataJObject.ToObject<DiscogsRetriever.DiscogsRetrieverData>(serializer);
                        break;
                    case Retriever.MusicBrainz:
                        commentData.RetrieverData = retrieverDataJObject.ToObject<MusicBrainzRetriever.MusicBrainzRetrieverData>(serializer);
                        break;
                    case Retriever.Lastfm:
                        commentData.RetrieverData = retrieverDataJObject.ToObject<LastfmRetriever.LastfmRetrieverData>(serializer);
                        break;
                    default:
                        throw new NotImplementedException($"Unsupported Retriever: {source}");
                }
            }
            else
            {
                throw new JsonSerializationException("RetrieverData or Source field is missing");
            }

            return commentData;
        }

        public override bool CanRead => true;
        public override bool CanWrite => false;
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
        public CommentData CommentData;

        private DummyFile() { }

        public static DummyFile FromNowPlaying(MusicBeeApiInterface mbApi, DummySongFields flags, CommentData data)
        {
            DummyFile song = new DummyFile();

            song.FileUrl = mbApi.NowPlaying_GetFileUrl();

            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), Plugin.CACHE_FOLDER);

            if (!Utils.IsInDirectory(song.FileUrl, cachePath))
            {
                throw new InvalidOperationException("File is not in cache folder.");
            }

            song.CommentData = data;

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

                song.CommentData = JsonConvert.DeserializeObject<CommentData>(comment); ;
            }
            else
            {
                song.CommentData = data;
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

            if (CommentData != null)
            {
                mbApi.Library_SetFileTag(FileUrl, MetaDataType.Comment, JsonConvert.SerializeObject(CommentData));
            }

            if (Image != null)
                mbApi.Library_SetArtworkEx(FileUrl, 0, Image);

            mbApi.Library_CommitTagsToFile(FileUrl);
        }
    }
}
