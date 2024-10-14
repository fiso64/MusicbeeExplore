using MusicBeePlugin.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
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