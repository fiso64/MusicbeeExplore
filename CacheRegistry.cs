using MusicBeePlugin.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.Services
{
    public class CacheRegistry
    {
        private Dictionary<string, string> dict;
        private readonly string path;
        private bool loaded = false;

        public CacheRegistry(string path)
        {
            this.path = path;
        }

        private void Load()
        {
            if (!File.Exists(path))
            {
                dict = new Dictionary<string, string>();
            }
            else
            {
                dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
            }
            
            loaded = true;
        }

        public void Save()
        {
            if (!loaded || dict == null || dict.Count == 0)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(dict));
        }

        private string ToKey(string query, Retriever source, MbeType action)
        {
            return $"{(int)source};{(int)action};{query.Trim().ToLower()}";
        }

        private bool TryGetValue(string query, Retriever source, MbeType action, out string group)
        {
            return dict.TryGetValue(ToKey(query, source, action), out group);
        }

        public bool Remove(string query, Retriever source, MbeType action)
        {
            return dict.Remove(ToKey(query, source, action));
        }

        public bool HasAnyCache(string query, Retriever source, MbeType action, out string group)
        {
            if (!loaded)
                Load();

            if (!TryGetValue(query, source, action, out group))
                return false;

            bool hasFiles = CacheGroupHasFiles(group);

            if (!hasFiles)
            {
                Remove(query, source, action);
                return false;
            }

            return true;
        }

        public void Add(string query, Retriever source, MbeType action, string group)
        {
            if (!loaded)
                Load();
            dict[ToKey(query, source, action)] = group;
        }

        public static string GetCacheGroup(MbeType mbeType, string entity, MbeSubgroup subgroup = MbeSubgroup.None)
        {
            mbeType = mbeType == MbeType.PopularTracks ? MbeType.MoreAlbums : mbeType;
            string subgroupStr = subgroup == MbeSubgroup.None ? string.Empty : subgroup.ToString();
            return $"{mbeType}_{entity.ToLower()}_{subgroupStr}";
        }

        public static void OpenCacheGroup(string group, bool newTab)
        {
            if (string.IsNullOrEmpty(group))
                throw new ArgumentException("Group cannot be null or empty.");

            if (newTab)
            {
                SendKeys.SendWait("^t");
            }
            Plugin.mbApi.MB_OpenFilterInTab(MetaDataType.Comment, ComparisonType.Contains, Plugin.IDENTIFIER, MetaDataType.Comment, ComparisonType.Contains, group);
        }

        public static bool CacheGroupHasFiles(string group)
        {
            if (string.IsNullOrEmpty(group))
                throw new ArgumentException("Group cannot be null or empty.");

            var query = MusicBeeHelpers.ConstructLibraryQuery(
                (MetaDataType.Comment, ComparisonType.Contains, Plugin.IDENTIFIER),
                (MetaDataType.Comment, ComparisonType.Contains, group)
            );

            mbApi.Library_QueryFilesEx(query, out string[] files);

            return files != null && files.Length > 0;
        }
    }
}
