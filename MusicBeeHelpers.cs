using MusicBeePlugin.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using static MusicBeePlugin.Plugin;
using System.Linq.Expressions;

namespace MusicBeePlugin
{
    public static class MusicBeeHelpers
    {
        private static MethodInfo invokeApplicationCommandMethod;
        private static MethodInfo deleteFileFromLibraryMethod;
        private static Type Struct118;
        private static Type Struct147;
        private static Type Class676;
        private static Func<string, object> createStruct118;
        private static Func<object, object> createClass676;
        private static FieldInfo pluginCommandsField;

        public static void LoadMethods() // Note: this can be made faster
        {
            var allFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.GetField | BindingFlags.SetField | BindingFlags.GetProperty | BindingFlags.SetProperty;

            var mbAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName.Contains("MusicBee"));

            void getInvokeMethod()
            {
                foreach (var refType in mbAsm.GetTypes())
                {
                    invokeApplicationCommandMethod = refType.GetMethods(allFlags).FirstOrDefault(m =>
                    {
                        var parameters = m.GetParameters();
                        return parameters.Length == 3
                            && parameters[0].ParameterType.Name == "ApplicationCommand"
                            && parameters[1].ParameterType == typeof(object)
                            && parameters[2].ParameterType.IsGenericType
                            && parameters[2].ParameterType.GetGenericTypeDefinition() == typeof(IList<>);
                    });

                    if (invokeApplicationCommandMethod != null)
                        break;
                }
            }

            void getDeleteMethod()
            {
                foreach (var refType in mbAsm.GetTypes())
                {
                    deleteFileFromLibraryMethod = refType.GetMethods(allFlags).FirstOrDefault(m =>
                    {
                        var parameters = m.GetParameters();
                        return m.ReturnType.IsGenericType
                            && m.ReturnType.GetGenericTypeDefinition() == typeof(List<>)
                            && parameters.Length == 3
                            && parameters[0].ParameterType.IsValueType
                            && parameters[1].ParameterType.IsGenericType
                            && parameters[1].ParameterType.GetGenericTypeDefinition() == typeof(List<>)
                            && parameters[2].ParameterType.IsEnum;
                    });

                    if (deleteFileFromLibraryMethod != null)
                        break;
                }

                var paramTypes = deleteFileFromLibraryMethod.GetParameters().Select(p => p.ParameterType).ToArray();

                Class676 = deleteFileFromLibraryMethod.ReturnType.GetGenericArguments()[0];

                Struct147 = paramTypes[0];

                Struct118 = Class676.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(f => f.FieldType)
                    .FirstOrDefault(t =>
                        t.IsValueType &&
                        t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Length == 1 &&
                        t.GetFields(BindingFlags.NonPublic | BindingFlags.Static).Length == 2 &&
                        t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)[0].FieldType == typeof(string) &&
                        t.GetFields(BindingFlags.NonPublic | BindingFlags.Static).All(f => f.FieldType == typeof(char[])));

                var struct118Ctor = Struct118.GetConstructor(new[] { typeof(string) });
                var class676Ctor = Class676.GetConstructor(new[] { Struct118 });

                // this should be faster than CreateInstance
                var param = Expression.Parameter(typeof(string));
                var body = Expression.New(struct118Ctor, param);
                var convert = Expression.Convert(body, typeof(object));
                createStruct118 = Expression.Lambda<Func<string, object>>(convert, param).Compile();

                param = Expression.Parameter(typeof(object));
                convert = Expression.Convert(param, Struct118);
                body = Expression.New(class676Ctor, convert);
                createClass676 = Expression.Lambda<Func<object, object>>(body, param).Compile();
            }

            void getPluginCommandsField()
            {
                foreach (var refType in mbAsm.GetTypes())
                {
                    pluginCommandsField = refType.GetFields(allFlags).FirstOrDefault(f =>
                        f.IsStatic && f.FieldType.IsGenericType
                        && f.FieldType.GetGenericTypeDefinition() == typeof(List<>)
                        && f.FieldType.GenericTypeArguments.Length == 1
                        && f.FieldType.GenericTypeArguments[0].IsGenericType
                        && f.FieldType.GenericTypeArguments[0].GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
                        && f.FieldType.GenericTypeArguments[0].GenericTypeArguments[0] == typeof(string)
                        && f.FieldType.GenericTypeArguments[0].GenericTypeArguments[1] == typeof(EventHandler)
                    );
            
                    if (pluginCommandsField != null)
                        break;
                }
            }

            getInvokeMethod();
            getDeleteMethod();
            getPluginCommandsField();
        }

        public static void InvokeApplicationCommand(ApplicationCommand command)
        {
            invokeApplicationCommandMethod.Invoke(null, new object[] { command, null, null });
        }

        // The commands can be invoked by calling handler(musicBeeApiInterface, EventArgs.Empty)
        public static List<KeyValuePair<string, EventHandler>> GetPluginCommands()
        {
            return (List<KeyValuePair<string, EventHandler>>)pluginCommandsField.GetValue(null);
        }
        
        public static void InvokePluginCommandByName(string command)
        {
            int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(command);
            hash = hash < 0 ? hash : -hash;
            invokeApplicationCommandMethod.Invoke(null, new object[] { (ApplicationCommand)hash, null, null });
        }

        public static void DeleteFilesFromLibrary(string[] paths)
        {
            var list_0 = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Class676));
            object struct147_0 = Activator.CreateInstance(Struct147);

            foreach (var path in paths)
            {
                var struct118 = createStruct118(path);
                var class676 = createClass676(struct118);
                list_0.Add(class676);
            }

            deleteFileFromLibraryMethod.Invoke(null, new[] { struct147_0, list_0, 0 });
        }

        public static string ConstructLibraryQuery(params (MetaDataType Field, ComparisonType Comparison, string Value)[] conditions)
        {
            var dict = new Dictionary<MetaDataType, string>()
            {
                { MetaDataType.Artist, "ArtistPeople" },
                { MetaDataType.Album, "Album" },
                { MetaDataType.AlbumArtist, "AlbumArtist" },
                { MetaDataType.TrackTitle, "Title" },
                { MetaDataType.Comment, "Comment" },
            };

            var query = new XElement("SmartPlaylist",
                new XElement("Source",
                    new XAttribute("Type", 1),
                    new XElement("Conditions",
                        new XAttribute("CombineMethod", "All"),
                        conditions.Select(c => new XElement("Condition",
                            new XAttribute("Field", dict[c.Field]),
                            new XAttribute("Comparison", c.Comparison.ToString()),
                            new XAttribute("Value", c.Value)
                        ))
                    )
                )
            );

            return query.ToString(SaveOptions.DisableFormatting);
        }

        public static void Pause()
        {
            var state = mbApi.Player_GetPlayState();
            if (state == PlayState.Playing || state == PlayState.Loading)
            {
                mbApi.Player_PlayPause();
            }
        }

        public static string GetSearchBoxTextIfFocused()
        {
            var focus = WinApiHelpers.GetFocus();

            if (WinApiHelpers.GetClassNN(focus).Contains("EDIT"))
            {
                return WinApiHelpers.GetText(focus);
            }

            return null;
        }

        public static (string artist, string title, string album, string albumArtist) GetFirstSelected()
        {
            mbApi.Library_QueryFilesEx("domain=SelectedFiles", out string[] files);

            if (files == null || files.Length == 0)
            {
                return default;
            }

            string artist = DummyProcessor.RemoveIdentifier(mbApi.Library_GetFileTag(files[0], MetaDataType.Artist));
            string title = mbApi.Library_GetFileTag(files[0], MetaDataType.TrackTitle);
            string album = mbApi.Library_GetFileTag(files[0], MetaDataType.Album);
            string albumArtist = DummyProcessor.RemoveIdentifier(mbApi.Library_GetFileTag(files[0], MetaDataType.AlbumArtist));
            return (artist, title, album, albumArtist);
        }

        // todo: Check if the order of these elements changes across different versions of musicbee
        // If so, also get this enum using reflection
        public enum ApplicationCommand
        {
            None,
            RatingSelectedFiles0,
            RatingSelectedFiles05,
            RatingSelectedFiles1,
            RatingSelectedFiles15,
            RatingSelectedFiles2,
            RatingSelectedFiles25,
            RatingSelectedFiles3,
            RatingSelectedFiles35,
            RatingSelectedFiles4,
            RatingSelectedFiles45,
            RatingSelectedFiles5,
            RatingNowPlaying0,
            RatingNowPlaying05,
            RatingNowPlaying1,
            RatingNowPlaying15,
            RatingNowPlaying2,
            RatingNowPlaying25,
            RatingNowPlaying3,
            RatingNowPlaying35,
            RatingNowPlaying4,
            RatingNowPlaying45,
            RatingNowPlaying5,
            EditSelectAll,
            EditClickedColumn,
            EditProperties,
            EditPropertiesPlaying,
            EditReopen,
            EditSave,
            EditSaveGotoNext,
            EditUndo,
            EditOpenTagInspector,
            EditCustomSearch,
            EditHighlightingRules,
            EditPreferences,
            EditTimestamp,
            EditConfigureCustomTags,
            EditCut,
            EditCopy,
            EditCopyAll,
            EditPaste,
            EditCropSelectedFiles,
            FileScanNewMedia,
            GeneralReloadHard,
            ToolsRescan,
            FileOpenUrl,
            GeneralResetFilters,
            GeneralGoBack,
            GeneralGoForward,
            GeneralGotoSearch,
            GeneralGotoNowPlayingTrack,
            GeneralLocateInCurrentNowPlayingTrack,
            GeneralLocateSelectedTrack,
            FileOpenWindowsExplorer,
            GeneralLocateInComputerSelectedTrack,
            GeneralReload,
            GeneralRefreshSmartPlaylist,
            GeneralRestart,
            GeneralExitApplication,
            GeneralActivateAutoShutdown,
            GeneralToggleSearchScope,
            MultiMediaNext,
            MultiMediaPlayPause,
            MultiMediaPrevious,
            MultiMediaStop,
            NowPlayingBookmark,
            NowPlayingListClear,
            NowPlayingListClearBefore,
            PlaybackNextTrack,
            PlaybackJumpToRandom,
            PlaybackNextAlbum,
            PlaybackPlayPause,
            PlaybackPreviousTrack,
            PlaybackPreviousAlbum,
            PlaybackSkipBack,
            PlaybackSkipForward,
            PlaybackMediumSkipBack,
            PlaybackMediumSkipForward,
            PlaybackLargeSkipBack,
            PlaybackLargeSkipForward,
            PlaybackStop,
            PlaybackStopAfterCurrent,
            PlaybackPlayNow,
            PlaybackPlayAlbumNow,
            PlaybackPlayNext,
            PlaybackPlayLast,
            PlaybackToggleSkip,
            PlaybackStartAutoDj,
            PlaybackPlayAllShuffled,
            PlaybackPlayAllInPanelShuffled,
            GeneralToggleShuffle,
            GeneralToggleRepeatMode,
            PlaybackToggleReplayGain,
            PlaybackReplayGainAlbum,
            PlaybackReplayGainSmart,
            PlaybackReplayGainTrack,
            PlaybackVolumeMute,
            PlaybackVolumeDown,
            PlaybackVolumeUp,
            PlaybackTempoDecrease,
            PlaybackTempoIncrease,
            PlaybackTempoReset,
            PlaybackTempoAssignPreset,
            PlaybackTempoUsePreset,
            GeneralToggleEqualiser,
            GeneralToggleDsp,
            RatingSelectedFilesTickDown,
            RatingSelectedFilesTickUp,
            RatingSelectedFilesNone,
            RatingSelectedFilesToggleLove,
            RatingNowPlayingTickDown,
            RatingNowPlayingTickUp,
            RatingNowPlayingNone,
            RatingNowPlayingToggleLove,
            SendToCommandsStart,
            SendToAutoDj,
            SendToClipboard,
            SendToClipboardNowPlaying,
            SendToExternalService,
            SendToExternalService2,
            SendToExternalService3,
            SendToExternalService4,
            SendToExternalService5,
            SendToExternalService6,
            SendToExternalService7,
            SendToExternalService8,
            SendToExternalServiceNowPlaying,
            SendToLibrary,
            SendToInbox,
            SendToOrganisedFolder,
            SendToOrganisedFolderCopy,
            SendToActiveDevice,
            SendToActivePlaylist,
            SendPlayingToActivePlaylist,
            SendToAudioBooks,
            SendToPodcasts,
            SendToVideo,
            SendToNewPlaylist,
            SendToExternalAudioEditor,
            SendToFolderMove,
            SendToFolderCopy,
            SendToReplaceFileSelectSource,
            SendToReplaceFileSelectTarget,
            SendToPlaylistAdd,
            SendToPlaylistRemove,
            SendToCommandsEnd,
            FileNewTab,
            FileCloseTab,
            FileNewPlaylistInTab,
            ToolsTagSearchAndReplace,
            ToolsFindArtwork,
            ToolsAlbumArtworkManager,
            ToolsArtistThumbManager,
            ToolsAutoNumber,
            ToolsAutoOrganise,
            ToolsAutoTagAlbum,
            ToolsAutoTagAll,
            ToolsAutoTagMissingTags,
            ToolsAutoTagMissingPictures,
            ToolsAutoTagInfer,
            ToolsConvertFormat,
            ToolsLocateMissingFiles,
            ToolsAnalyseVolume,
            ToolsUndoLevelVolume,
            ToolsRipCd,
            ToolsSyncPlayCountLastFm,
            WebDownloadNow,
            WebOpenLink0SelectedFile,
            WebOpenLink1SelectedFile,
            WebOpenLink2SelectedFile,
            WebOpenLink3SelectedFile,
            WebOpenLink4SelectedFile,
            WebOpenLink5SelectedFile,
            WebOpenLink6SelectedFile,
            WebOpenLink7SelectedFile,
            WebOpenLink8SelectedFile,
            WebOpenLink9SelectedFile,
            WebOpenLink10SelectedFile,
            WebOpenLink11SelectedFile,
            WebOpenLink12SelectedFile,
            WebOpenLink13SelectedFile,
            WebOpenLink14SelectedFile,
            ViewApplicationMinimise,
            GeneralShowMainPanel,
            ViewLayoutArtwork,
            ViewLayoutArtists,
            ViewLayoutAlbumAndTracks,
            ViewLayoutJukebox,
            ViewLayoutTrackDetails,
            ViewEqualiser,
            ViewResetTrackBrowser,
            ViewResetThumbBrowser,
            ViewLyricsFloating,
            ViewNowPlayingNotification,
            ViewNowPlayingTrackFinder,
            ViewToggleShowHeaderBar,
            ViewToggleLockDown,
            ViewToggleLeftPanel,
            ViewToggleLeftMainPanel,
            ViewToggleRightMainPanel,
            ViewToggleRightPanel,
            ViewToggleShowUpcomingTracks,
            ViewToggleVisualiser,
            ViewVisualiserToggleFullScreen,
            ViewFilesToEdit,
            ViewToggleMiniPlayer,
            ViewToggleMicroPlayer,
            ViewJumpList,
            ViewDecreaseLyricsFont,
            ViewIncreaseLyricsFont,
            ViewShowFullSizeArtwork,
            ViewPauseArtworkRotation,
            ViewToggleNowPlayingBar,
            SetMainPanelLayout,
            GeneralSetFilter,
            GeneralToggleScrobbling,
            RatingNowPlayingToggleBan,
            RatingSelectedFilesToggleBan,
            GeneralLocateFileInPlaylist,
            GeneralFindSimilarArtist,
            GeneralLocateInComputerNowPlayingTrack,
            LibraryCreateRadioLink,
            LibraryCreatePodcastSubscription,
            WebSearchPodcasts,
            ViewApplicationActivate,
            DeviceSafelyRemove,
            ViewSkinsSelect,
            ViewSkinsToggleBorder,
            FileCreateLibrary,
            FileNewPlaylistFolder,
            FileNewPlaylist,
            FileNewAutoPlaylist,
            FileNewRadioPlaylist,
            FileNewMusicFolder,
            EditPlaylist,
            PlaylistExport,
            DeviceSynchronise,
            DeviceCopyPlaylist,
            GeneralShowTrackInfo,
            GeneralSelectVolume,
            GeneralShowMiniPlayer,
            PlaylistSave,
            PlaylistClear,
            PlaylistShuffle,
            PlaylistRestoreOrder,
            PlaylistUpdateOrder,
            ToolsRemoveDuplicates,
            ToolsRemoveDeadLinks,
            ToolsManageDuplicates,
            ToolsBurnCd,
            ToolsTagCapitalise,
            ToolsTagResetPlayCount,
            ToolsTagResetSkipCount,
            ToolsSyncLastFmLovedTracks,
            ToolsAutoTagMissingLyrics,
            DeviceCopySelectedFiles,
            EditPasteArtistPicture,
            EditPasteAlbumPicture,
            EditRemoveTags,
            PlaybackVolumeGoto,
            ViewToggleVerticalTagEditor,
            ViewCollapseNodes,
            GeneralGotoTab1,
            GeneralGotoTab2,
            GeneralGotoTab3,
            GeneralGotoTab4,
            GeneralGotoTab5,
            GeneralGotoTab6,
            GeneralGotoTab7,
            GeneralGotoTab8,
            GeneralGotoTab9,
            ViewShowFilter = 32768
        }
    }
}
