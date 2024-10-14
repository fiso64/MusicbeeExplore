using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicBeePlugin.Models;
using MusicBeePlugin.Retrievers;
using Newtonsoft.Json;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.Commands
{
    public class LoadSelectedAlbumsCommand : ICommand
    {
        public async Task Execute()
        {
            mbApi.Library_QueryFilesEx("domain=SelectedFiles", out string[] files);

            if (files == null || files.Length == 0)
            {
                MessageBox.Show("No files selected");
                return;
            }

            foreach (var file in files)
            {
                var dummy = DummyFile.FromPath(mbApi, file, DummySongFields.Album | DummySongFields.AlbumArtist | DummySongFields.Year | DummySongFields.Image);

                if (dummy == null || dummy.RetrieverData.State != State.UnloadedAlbum)
                {
                    continue;
                }

                await Plugin.dummyProcessor.ProcessUnloadedAlbum(dummy);
            }
        }
    }
}