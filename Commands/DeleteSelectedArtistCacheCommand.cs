using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.Commands
{
    public class DeleteSelectedArtistCacheCommand : ICommand
    {
        public async Task Execute()
        {
            string artistQuery = null;
            mbApi.Library_QueryFilesEx("domain=SelectedFiles", out string[] files);
            if (files != null && files.Length > 0)
                artistQuery = mbApi.Library_GetFileTag(files[0], MetaDataType.AlbumArtist);

            if (string.IsNullOrEmpty(artistQuery))
            {
                MessageBox.Show("No artist selected");
                return;
            }

            artistQuery = artistQuery.Split(';')[0];

            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), Plugin.CACHE_FOLDER);
            string artistPath = Path.Combine(cachePath, artistQuery.SafeFileName());

            if (Directory.Exists(artistPath))
            {
                try
                {
                    Directory.Delete(artistPath, true);
                    MessageBox.Show($"Cache deleted for artist: {artistQuery}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deleting artist cache: {ex.Message}");
                    MessageBox.Show($"Error deleting artist cache: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show($"No cache found for artist: {artistQuery}");
            }

            mbApi.MB_RefreshPanels();
        }
    }
}