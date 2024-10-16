using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.ComponentModel;

using MusicBeePlugin.Models;
using MusicBeePlugin.Commands;
using MusicBeePlugin.Services;
using System.Linq;

// todo: Why does running Startup() in ReceiveNotification() instead of in Initialise() break everything?
//       Album requests never finish (only happens when playing the dummy album file, not when
//       using the command to load the album). Also, unloaded tracks never play, although they are downloaded
//       successfully.

// bug (minor): Querying an artist > exiting musicbee > re-opening musicbee > loading one of the albums (without requeriyng the artist)
//       the album tracks will not appear. Must reopen the filter to make them appear. Seems like a MusicBee bug, but can be fixed on
//       the plugin side by keeping track of whether the artist has been queried in the current session, and if not, reopening the filter
//       when loading an album

// todo: cancel download when track changes / synchronize access to downloader
// todo: preload next track when current playback is almost done (maybe)
// todo: make track playback faster
// todo: try to reduce false youtube downloads
// todo: bandcamp retrieval and playback
// todo: soundcloud playback
// todo: bandcamp and discogs album suggestions

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        public const string IDENTIFIER = "巽 ";
        public const string CACHE_FOLDER = "MusicBeeExplore/cache";
        public const string CACHE_HIDDEN_FOLDER = "MusicBeeExplore/cache-hidden";
        public const string CACHE_MAP_FILE = "MusicBeeExplore/cache/cache.json";
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
        public static ICommand getExtendedAlbumsForSelectedArtistDiscogsCommand;
        public static ICommand getExtendedAlbumsForSelectedArtistMusicBrainzCommand;
        public static ICommand loadSelectedAlbumsCommand;
        public static ICommand toggleCachedAlbumsCommand;
        public static ICommand getPopularTracksForArtistCommand;
        public static ICommand getSimilarAlbumsCommand;
        public static ICommand getShareLinkCommand;

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
            getExtendedAlbumsForSelectedArtistDiscogsCommand = new GetAlbumsForSelectedArtistCommand(Retriever.Discogs, 1);
            getExtendedAlbumsForSelectedArtistMusicBrainzCommand = new GetAlbumsForSelectedArtistCommand(Retriever.MusicBrainz, 1);
            loadSelectedAlbumsCommand = new LoadSelectedAlbumsCommand();
            toggleCachedAlbumsCommand = new ToggleCachedAlbumsCommand();
            getPopularTracksForArtistCommand = new GetPopularTracksForArtistCommand();
            getSimilarAlbumsCommand = new GetSimilarAlbumsCommand();
            getShareLinkCommand = new GetShareLinkCommand();

            var menu = (ToolStripMenuItem)mbApi.MB_AddMenuItem($"mnuTools/MBExplore: Selected", null, null);

            void addCommand(string menuName, string description, ICommand cmd)
            {
                menu.DropDown.Items.Add(menuName, null, async (s, e) => await cmd.Execute());
                mbApi.MB_RegisterCommand(description, async (sender, e) => await cmd.Execute());
            }

            addCommand("Discogs Query", "MusicBeeExplore: Discogs: Query selected or search box artist", getAlbumsForSelectedArtistDiscogsCommand);
            addCommand("MusicBrainz Query", "MusicBeeExplore: MusicBrainz: Query selected or search box artist", getAlbumsForSelectedArtistMusicBrainzCommand);
            addCommand("Discogs Extended Query", "MusicBeeExplore: Discogs: Extended query selected or search box artist", getExtendedAlbumsForSelectedArtistDiscogsCommand);
            addCommand("MusicBrainz Extended Query", "MusicBeeExplore: MusicBrainz: Extended query selected or search box artist", getExtendedAlbumsForSelectedArtistMusicBrainzCommand);
            addCommand("Load Selected Albums", "MusicBeeExplore: Load selected albums", loadSelectedAlbumsCommand);
            addCommand("Toggle Cached Albums", "MusicBeeExplore: Toggle cached albums", toggleCachedAlbumsCommand);
            addCommand("Get Popular Tracks", "MusicBeeExplore: Last.fm: Get popular tracks for selected artist", getPopularTracksForArtistCommand);
            addCommand("Get Similar Albums", "MusicBeeExplore: Last.fm: Get similar albums for selected album", getSimilarAlbumsCommand);
            addCommand("Get Share Link", "MusicBeeExplore: Get YouTube share link for selected track", getShareLinkCommand);
        }

        public bool Configure(IntPtr panelHandle)
        {
            tempConfig = new Config(config);

            if (panelHandle != IntPtr.Zero)
            {
                Panel configPanel = (Panel)Panel.FromHandle(panelHandle);
                const int horizontalEditOffset = 140;
                const int editWidth = 200;
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
                        Location = new Point(horizontalEditOffset, 0),
                        Width = editWidth,
                        Height = 15,
                    };
                    textBox.Text = initialText;
                    textBox.TextChanged += (sender, e) => onTextChanged(textBox.Text);
                    textBoxPanel.Controls.Add(textBox);

                    configPanel.Controls.Add(textBoxPanel);
                    verticalOffset += textBoxPanel.Height;
                }

                void addDropdown(string labelText, Enum selectedValue, Type enumType, Action<Enum> onSelectedIndexChanged)
                {
                    Panel dropdownPanel = new Panel
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
                    dropdownPanel.Controls.Add(label);

                    ComboBox comboBox = new ComboBox
                    {
                        Location = new Point(horizontalEditOffset, 0),
                        Width = editWidth,
                        DropDownStyle = ComboBoxStyle.DropDownList
                    };

                    comboBox.Items.Clear();
                    foreach (Enum enumValue in Enum.GetValues(enumType))
                    {
                        string description = GetEnumDescription(enumValue);
                        comboBox.Items.Add(new EnumWrapper(enumValue, description));
                    }

                    comboBox.DisplayMember = "Display";
                    comboBox.ValueMember = "Value";
                    comboBox.SelectedItem = comboBox.Items.Cast<EnumWrapper>().FirstOrDefault(item => item.Value.Equals(selectedValue));
                    comboBox.SelectedIndexChanged += (sender, e) => onSelectedIndexChanged(((EnumWrapper)comboBox.SelectedItem).Value);

                    dropdownPanel.Controls.Add(comboBox);

                    configPanel.Controls.Add(dropdownPanel);
                    verticalOffset += dropdownPanel.Height;
                }

                addCheckbox("Open results in new tab", config.OpenInNewTab, (chk) => tempConfig.OpenInNewTab = chk);
                addCheckbox("Show download window", config.ShowDownloadWindow, (chk) => tempConfig.ShowDownloadWindow = chk);
                addCheckbox("Queue tracks after loading album", config.QueueTracksAfterAlbumLoad, (chk) => tempConfig.QueueTracksAfterAlbumLoad = chk);
                addCheckbox("Get popular tracks when loading albums", config.GetPopularTracks, (chk) => tempConfig.GetPopularTracks = chk);

                verticalOffset += 10;

                addDropdown("On Play Action:", config.OnPlay, typeof(Config.PlayAction), (selected) => tempConfig.OnPlay = (Config.PlayAction)selected);
                addTextBox("Player command:", config.MediaPlayerCommand, (text) => tempConfig.MediaPlayerCommand = text);
                addTextBox("Discogs Token:", config.DiscogsToken, (text) => tempConfig.DiscogsToken = text);
                addTextBox("Last.fm API Key:", config.LastfmApiKey, (text) => tempConfig.LastfmApiKey = text);
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

        private class EnumWrapper
        {
            public Enum Value { get; }
            public string Display { get; }

            public EnumWrapper(Enum value, string display)
            {
                Value = value;
                Display = display;
            }
        }

        private string GetEnumDescription(Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (DescriptionAttribute)Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));
            return attribute == null ? value.ToString() : attribute.Description;
        }
    }
}
