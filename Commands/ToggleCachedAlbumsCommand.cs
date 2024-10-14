using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.Commands
{
    public class ToggleCachedAlbumsCommand : ICommand
    {
        public async Task Execute()
        {
            string cachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_FOLDER);
            string hiddenCachePath = Path.Combine(mbApi.Setting_GetPersistentStoragePath(), CACHE_HIDDEN_FOLDER);

            if (Directory.Exists(cachePath))
            {
                await HideCachedAlbums(cachePath, hiddenCachePath);
            }
            else if (Directory.Exists(hiddenCachePath))
            {
                await ShowCachedAlbums(hiddenCachePath, cachePath);
            }

            mbApi.MB_RefreshPanels();
        }

        private async Task HideCachedAlbums(string cachePath, string hiddenCachePath)
        {
            try
            {
                if (Directory.Exists(hiddenCachePath))
                    Directory.Delete(hiddenCachePath, true);
                Directory.Move(cachePath, hiddenCachePath);
                MessageBox.Show("Cached albums hidden successfully.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error hiding cached albums: {ex.Message}");
                MessageBox.Show($"Error hiding cached albums: {ex.Message}");
            }
        }

        private async Task ShowCachedAlbums(string hiddenCachePath, string cachePath)
        {
            try
            {
                Directory.Move(hiddenCachePath, cachePath);
                await AddCachedAlbumsToLibrary(cachePath);
                MessageBox.Show("Cached albums shown successfully.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing cached albums: {ex.Message}");
                MessageBox.Show($"Error showing cached albums: {ex.Message}");
            }
        }

        private async Task AddCachedAlbumsToLibrary(string cachePath)
        {
            var files = Directory.GetFiles(cachePath, "*.*", SearchOption.AllDirectories);
            var dirs = Directory.EnumerateDirectories(cachePath, "*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                if (!Utils.IsMusicFile(file))
                    continue;

                mbApi.Library_AddFileToLibrary(file, LibraryCategory.Music);
            }

            foreach (var dir in dirs)
            {
                byte[] image = null;

                if (File.Exists(Path.Combine(dir, "cover.jpg")))
                {
                    image = File.ReadAllBytes(Path.Combine(dir, "cover.jpg"));
                }

                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (!Utils.IsMusicFile(file))
                        continue;

                    mbApi.Library_AddFileToLibrary(file, LibraryCategory.Music);

                    if (image != null)
                        mbApi.Library_SetArtworkEx(file, 0, image);
                }
            }
        }
    }
}