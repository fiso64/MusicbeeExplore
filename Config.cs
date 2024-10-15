using System;
using System.IO;
using Newtonsoft.Json;

namespace MusicBeePlugin
{
    public class Config
    {
        private readonly string ConfigFilePath = null;

        public bool OpenInNewTab { get; set; } = false;
        public bool ShowDownloadWindow { get; set; } = true;
        public bool QueueTracksAfterAlbumLoad { get; set; } = false;
        public bool GetPopularTracks { get; set; } = false;
        public bool UseMediaPlayer { get; set; } = false;
        public string DiscogsToken { get; set; } = null;
        public string LastfmApiKey { get; set; } = null;
        public string MediaPlayerCommand { get; set; } = "mpv {url} --no-video";

        public Config() { }

        public Config(string path)
        {
            ConfigFilePath = path;
        }

        public Config(Config other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            foreach (var property in typeof(Config).GetProperties())
            {
                if (property.CanWrite)
                {
                    property.SetValue(this, property.GetValue(other));
                }
            }

            ConfigFilePath = other.ConfigFilePath;
        }

        public void Load()
        {
            if (string.IsNullOrWhiteSpace(ConfigFilePath))
                throw new ArgumentException("Config path cannot be null or empty.");

            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var config = JsonConvert.DeserializeObject<Config>(json);

                foreach (var property in typeof(Config).GetProperties())
                {
                    if (property.CanWrite)
                    {
                        property.SetValue(this, property.GetValue(config));
                    }
                }
            }
        }

        public void Save()
        {
            if (string.IsNullOrWhiteSpace(ConfigFilePath))
                throw new ArgumentException("Config path cannot be null or empty.");

            var json = JsonConvert.SerializeObject(this, Formatting.Indented);

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath));
            File.WriteAllText(ConfigFilePath, json);
        }

        public override bool Equals(object obj)
        {
            if (obj is Config other)
            {
                foreach (var property in typeof(Config).GetProperties())
                {
                    if (property.CanRead)
                    {
                        var thisValue = property.GetValue(this);
                        var otherValue = property.GetValue(other);
                        if (!object.Equals(thisValue, otherValue))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
            return false;
        }
    }

}
