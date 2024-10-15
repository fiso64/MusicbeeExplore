using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MusicBeePlugin.Models;
using Newtonsoft.Json;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.Services
{
    public class DummyCreator
    {
        private readonly string cachePath;
        private readonly string dummyPath;

        public DummyCreator(string cachePath, string dummyPath)
        {
            this.cachePath = cachePath;
            this.dummyPath = dummyPath;
        }

        public class DummyFileInfo
        {
            public string FilePath { get; set; }
            public Dictionary<MetaDataType, string> Tags { get; set; } = new Dictionary<MetaDataType, string>();
            public CommentData CommentData { get; set; }
            public byte[] Image { get; set; }
        }

        public void CreateDummyFile(DummyFileInfo info)
        {
            if (!File.Exists(dummyPath))
            {
                File.WriteAllBytes(dummyPath, DummyOpusFile);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(info.FilePath));
            File.Copy(dummyPath, info.FilePath, true);

            mbApi.Library_AddFileToLibrary(info.FilePath, LibraryCategory.Music);

            foreach (var tag in info.Tags)
            {
                string value = tag.Value;
                if ((tag.Key == MetaDataType.Artist || tag.Key == MetaDataType.AlbumArtist) && !value.StartsWith(IDENTIFIER))
                {
                    value = IDENTIFIER + value;
                }
                mbApi.Library_SetFileTag(info.FilePath, tag.Key, value);
            }

            mbApi.Library_SetFileTag(info.FilePath, MetaDataType.Comment, JsonConvert.SerializeObject(info.CommentData));

            mbApi.Library_CommitTagsToFile(info.FilePath);

            if (info.Image != null)
            {
                mbApi.Library_SetArtworkEx(info.FilePath, 0, info.Image);
            }
        }

        public async Task CreateDummyFiles<T>(
            IEnumerable<T> items,
            Func<T, int, DummyFileInfo> infoExtractor,
            Action<double> progressCallback,
            Func<bool> cancellationCheck)
        {
            Directory.CreateDirectory(cachePath);

            int totalItems = items.Count();
            int completedItems = 0;

            int index = 0;
            foreach (var item in items)
            {
                if (cancellationCheck())
                {
                    throw new OperationCanceledException();
                }

                var info = infoExtractor(item, index);
                CreateDummyFile(info);

                completedItems++;
                double progressPercentage = (completedItems / (double)totalItems) * 100;
                progressCallback(progressPercentage);

                index++;
            }
        }

        public static readonly byte[] DummyOpusFile = new byte[] 
        { 
            79, 103, 103, 83, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 225, 93, 143, 181, 0, 0, 0, 0, 160, 153, 222, 17, 1, 19, 79, 112, 
            117, 115, 72, 101, 97, 100, 1, 2, 56, 1, 128, 187, 0, 0, 0, 0, 0, 79, 103, 103, 83, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
            225, 93, 143, 181, 1, 0, 0, 0, 28, 9, 25, 48, 1, 60, 79, 112, 117, 115, 84, 97, 103, 115, 12, 0, 0, 0, 76, 97, 
            118, 102, 54, 49, 46, 49, 46, 49, 48, 48, 1, 0, 0, 0, 28, 0, 0, 0, 101, 110, 99, 111, 100, 101, 114, 61, 76, 97, 
            118, 99, 54, 49, 46, 51, 46, 49, 48, 48, 32, 108, 105, 98, 111, 112, 117, 115, 79, 103, 103, 83, 0, 4, 248, 94, 0,
            0, 0, 0, 0, 0, 225, 93, 143, 181, 2, 0, 0, 0, 120, 108, 26, 155, 26, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252,
            255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254,
            252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255,
            254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254 
        };
    }
}