using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;

using MusicBeePlugin.Models;
using MusicBeePlugin.Commands;
using MusicBeePlugin.Services;

// todo: fix song skipping bug
//       For some reason the plugin does not receive any notifications while downloading a song,
//       making it impossible to intercept and pause dummy songs

// todo: cancel download when track changes / synchronize access to downloader
// todo: preload next track when current playback is almost done (maybe)
// todo: make track playback faster
// todo: try to reduce false youtube downloads
// todo: bandcamp retrieval and playback
// todo: bandcamp and discogs album suggestions

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        public const string IDENTIFIER = "巽 ";
        public const string CACHE_FOLDER = "MusicBeeExplore/cache";
        public const string CACHE_HIDDEN_FOLDER = "MusicBeeExplore/cache-hidden";
        public const string CACHE_MAP_FILE = "MusicBeeExplore/cache.json";
        public const string CONFIG_FILE = "MusicBeeExplore/mbe.conf";
        public const string DUMMY_FILE = "MusicBeeExplore/cache/dummy.opus";

        private PluginInfo about = new PluginInfo();
        private static Config tempConfig = null;

        public static MusicBeeApiInterface mbApi;
        public static Config config;
        public static DummyCreator dummyManager;
        public static DummyProcessor dummyProcessor;
        public static CacheRegistry cacheRegistry;

        public static ICommand getAlbumsForSelectedArtistDiscogsCommand;
        public static ICommand getAlbumsForSelectedArtistMusicBrainzCommand;
        public static ICommand loadSelectedAlbumsCommand;
        public static ICommand toggleCachedAlbumsCommand;
        public static ICommand getPopularTracksForArtistCommand;
        public static ICommand getSimilarAlbumsCommand;

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
            about.ConfigurationPanelHeight = 180;

            Startup();

            return about;
        }

        public void Startup()
        {
            config = new Config(Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CONFIG_FILE));
            config.Load();

            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_FOLDER);
            string dummyPath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), DUMMY_FILE);
            string cacheMapPath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_MAP_FILE);
            dummyManager = new DummyCreator(cachePath, dummyPath);
            dummyProcessor = new DummyProcessor();
            cacheRegistry = new CacheRegistry(cacheMapPath);

            getAlbumsForSelectedArtistDiscogsCommand = new GetAlbumsForSelectedArtistCommand(Retriever.Discogs);
            getAlbumsForSelectedArtistMusicBrainzCommand = new GetAlbumsForSelectedArtistCommand(Retriever.MusicBrainz);
            loadSelectedAlbumsCommand = new LoadSelectedAlbumsCommand();
            toggleCachedAlbumsCommand = new ToggleCachedAlbumsCommand();
            getPopularTracksForArtistCommand = new GetPopularTracksForArtistCommand();
            getSimilarAlbumsCommand = new GetSimilarAlbumsCommand();

            var menu = (ToolStripMenuItem)mbApi.MB_AddMenuItem($"mnuTools/MBExplore: Selected", null, null);

            void addCommand(string menuName, string description, ICommand cmd)
            {
                menu.DropDown.Items.Add(menuName, null, async (s, e) => await cmd.Execute());
                mbApi.MB_RegisterCommand(description, async (sender, e) => await cmd.Execute());
            }

            addCommand("Discogs Query", "MusicBeeExplore: Discogs: Query selected or search box artist", getAlbumsForSelectedArtistDiscogsCommand);
            addCommand("MusicBrainz Query", "MusicBeeExplore: MusicBrainz: Query selected or search box artist", getAlbumsForSelectedArtistMusicBrainzCommand);
            addCommand("Load Selected Albums", "MusicBeeExplore: Load selected albums", loadSelectedAlbumsCommand);
            addCommand("Toggle Cached Albums", "MusicBeeExplore: Toggle cached albums", toggleCachedAlbumsCommand);
            addCommand("Get Popular Tracks", "MusicBeeExplore: Last.fm: Get popular tracks for selected artist", getPopularTracksForArtistCommand);
            addCommand("Get Similar Albums", "MusicBeeExplore: Last.fm: Get similar albums for selected album", getSimilarAlbumsCommand);
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
                    Panel textBoxPanel = new Panel
                    {
                        Location = new Point(0, verticalOffset),
                        Width = configPanel.Width,
                        Height = 25
                    };

                    Label label = new Label
                    {
                        Text = labelText,
                        AutoSize = true,
                        Location = new Point(0, 0)
                    };
                    textBoxPanel.Controls.Add(label);

                    TextBox textBox = new TextBox
                    {
                        Location = new Point(label.Width + 10, 0),
                        Width = 200,
                        Height = 15,
                    };
                    textBox.Text = initialText;
                    textBox.TextChanged += (sender, e) => onTextChanged(textBox.Text);
                    textBoxPanel.Controls.Add(textBox);

                    configPanel.Controls.Add(textBoxPanel);
                    verticalOffset += textBoxPanel.Height;
                }

                addCheckbox("Open results in new tab", config.OpenInNewTab, (chk) => tempConfig.OpenInNewTab = chk);
                addCheckbox("Show download window", config.ShowDownloadWindow, (chk) => tempConfig.ShowDownloadWindow = chk);
                addCheckbox("Queue tracks after loading album", config.QueueTracksAfterAlbumLoad, (chk) => tempConfig.QueueTracksAfterAlbumLoad = chk);
                addCheckbox("Get popular tracks when loading albums", config.GetPopularTracks, (chk) => tempConfig.GetPopularTracks = chk);
                addCheckbox("Use another player to stream audio", config.UseMediaPlayer, (chk) => tempConfig.UseMediaPlayer = chk);

                addTextBox("Player command: ", config.MediaPlayerCommand, (text) => tempConfig.MediaPlayerCommand = text);
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
            cacheRegistry.Save();
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
                //case NotificationType.PluginStartup:
                //    Startup();
                //    break;
                case NotificationType.TrackChanged:
                    Debug.WriteLine($"Now playing: {mbApi.NowPlaying_GetFileTag(MetaDataType.Artist)} - {mbApi.NowPlaying_GetFileTag(MetaDataType.TrackTitle)}");
                    dummyProcessor.ProcessPlayingTrack();
                    break;
            }
        }
    }
}