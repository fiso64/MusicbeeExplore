using System.Threading.Tasks;
using System.Windows.Forms;
using MusicBeePlugin.Models;
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

            int count = 0;

            foreach (var file in files)
            {
                var dummy = DummyFile.FromPath(mbApi, file, DummySongFields.Album | DummySongFields.AlbumArtist | DummySongFields.Year | DummySongFields.Image);

                if (dummy == null || dummy.CommentData.State != State.UnloadedAlbum)
                {
                    continue;
                }

                await Plugin.dummyProcessor.ProcessUnloadedAlbum(dummy);
                count++;
            }

            if (count == 0)
            {
                MessageBox.Show("No unloaded albums selected");
            }
        }
    }
}