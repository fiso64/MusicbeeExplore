using MusicBeePlugin.Models;
using MusicBeePlugin.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin
{
    public static class MusicBeeHelpers
    {
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

        //public void SetSearchBoxText(string query)
        //{
        //    if (searchBoxKey == Keys.None)
        //    {
        //        int idx = Array.FindIndex(mbSettings, s => s.Contains("<Command>GeneralGotoSearch</Command>"));
        //        string keyStr = mbSettings[idx - 1].Replace("<Key>", "").Replace("</Key>", "").Trim();
        //        searchBoxKey = (Keys)int.Parse(keyStr);
        //    }

        //    WinApiHelpers.SendKey(searchBoxKey);
        //    var h = WinApiHelpers.GetFocus();

        //    if (!WinApiHelpers.GetClassNN(h).Contains("EDIT"))
        //    {
        //        Debug.WriteLine(WinApiHelpers.GetClassNN(h));
        //        WinApiHelpers.SendKey(searchBoxKey);
        //        h = WinApiHelpers.GetFocus();

        //        if (!WinApiHelpers.GetClassNN(h).Contains("EDIT"))
        //        {
        //            Debug.WriteLine(WinApiHelpers.GetClassNN(h));
        //            return;
        //        }
        //    }

        //    WinApiHelpers.SetEditText(h, query);
        //}
    }
}