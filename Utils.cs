using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace MusicBeePlugin
{
    public static class Utils
    {
        public static string ConvertStringToId(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                string base64Hash = Convert.ToBase64String(hashBytes);
                string alphanumericId = base64Hash.Replace("+", "").Replace("/", "").Replace("=", "").Substring(0, 16);
                return alphanumericId;
            }
        }

        public static string EscapeQuotes(string arg)
        {
            return arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static async Task<bool> ExecuteFFmpegCommand(string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = "ffmpeg";
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string error = await process.StandardError.ReadToEndAsync();
                    Debug.WriteLine($"FFmpeg command failed: {error}");
                    return false;
                }

                return true;
            }
        }

        public static void PlayWithMpv(string url, bool wait = false)
        {
            // Prepare the command to run mpv
            string command = "mpv";
            string arguments = $"\"{Utils.EscapeQuotes(url.Trim())}\" --no-video";

            // Create a new process to run mpv
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = false // Set to false to show the mpv window
            };

            try
            {
                // Start the mpv process
                using (Process process = Process.Start(startInfo))
                {
                    // Optionally, you can wait for the process to exit
                    if (wait) process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while trying to play the video: {ex.Message}");
            }
        }

        public static string SafeFileName(this string fileName)
        {
            return string.Join("_", fileName.Split(Path.GetInvalidFileNameChars())).TrimEnd('.').Trim();
        }

        public static (string trackNo, string trackCount, string discNo, string discCount) ParseTrackAndDisc(string trackPos, string discPos)
        {
            var trackNo = trackPos ?? string.Empty;
            var trackCount = string.Empty;
            var discNo = discPos ?? string.Empty;
            var discCount = string.Empty;
            
            if (trackNo.Contains('/'))
            {
                var parts = trackNo.Split('/');
                trackNo = parts[0];
                trackCount = parts[1];
            }

            if (discNo.Contains('/'))
            {
                var parts = discNo.Split('/');
                discNo = parts[0];
                discCount = parts[1];
            }

            if (trackNo.Contains('-'))
            {
                var parts = trackNo.Split('-');
                discNo = parts[0];
                trackNo = parts[1];
            }

            return (trackNo, trackCount, discNo, discCount);
        }

        public static bool IsMusicFile(string filePath)
        {
            string[] musicExtensions = { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".opus" };
            return Array.Exists(musicExtensions, ext => filePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsInDirectory(string path, string directoryPath)
        {
            path = path.Replace('\\', '/');
            directoryPath = directoryPath.Replace('\\', '/').TrimEnd('/') + '/';
            return path.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase);
        }

        public static readonly byte[] DummyOpusFile = new byte[] { 79, 103, 103, 83, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 225, 93, 143, 181, 0, 0, 0, 0, 160, 153, 222, 17, 1, 19, 79, 112, 117, 115, 72, 101, 97, 100, 1, 2, 56, 1, 128, 187, 0, 0, 0, 0, 0, 79, 103, 103, 83, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 225, 93, 143, 181, 1, 0, 0, 0, 28, 9, 25, 48, 1, 60, 79, 112, 117, 115, 84, 97, 103, 115, 12, 0, 0, 0, 76, 97, 118, 102, 54, 49, 46, 49, 46, 49, 48, 48, 1, 0, 0, 0, 28, 0, 0, 0, 101, 110, 99, 111, 100, 101, 114, 61, 76, 97, 118, 99, 54, 49, 46, 51, 46, 49, 48, 48, 32, 108, 105, 98, 111, 112, 117, 115, 79, 103, 103, 83, 0, 4, 248, 94, 0, 0, 0, 0, 0, 0, 225, 93, 143, 181, 2, 0, 0, 0, 120, 108, 26, 155, 26, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254, 252, 255, 254 };
    }

    public class ProgressWindow : Form
    {
        private ProgressBar progressBar;
        private Label statusLabel;
        private CancellationTokenSource cancellationTokenSource;
        private readonly object lockObject = new object();

        public ProgressWindow(string title, IntPtr parentHandle)
        {
            InitializeComponents(title);
            SetLocation(parentHandle);
            cancellationTokenSource = new CancellationTokenSource();
            this.FormClosing += OnFormClosing; // Subscribe to FormClosing event
        }

        private void InitializeComponents(string title)
        {
            this.Text = title;
            this.Size = new Size(400, 150);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            progressBar = new ProgressBar()
            {
                Dock = DockStyle.Top,
                Maximum = 100,
                Height = 30
            };

            statusLabel = new Label()
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };

            this.Controls.Add(statusLabel);
            this.Controls.Add(progressBar);
        }

        private void SetLocation(IntPtr parentHandle)
        {
            if (parentHandle != IntPtr.Zero)
            {
                var parentWindow = Control.FromHandle(parentHandle);
                if (parentWindow != null)
                {
                    var parentBounds = parentWindow.Bounds;
                    this.StartPosition = FormStartPosition.Manual;
                    this.Location = new Point(
                        parentBounds.X + (parentBounds.Width - this.Width) / 2,
                        parentBounds.Y + (parentBounds.Height - this.Height) / 2
                    );
                }
            }
        }

        public void UpdateStatus(string status)
        {
            if (cancellationTokenSource.IsCancellationRequested)
                return;

            //this.BeginInvoke(new Action(() => statusLabel.Text = status));
            //lock (lockObject)
            //statusLabel.Text = status;

            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => statusLabel.Text = status));
            }
            else
            {
                statusLabel.Text = status;
            }
        }

        public void UpdateProgress(double percentage)
        {
            if (cancellationTokenSource.IsCancellationRequested)
                return;

            //this.BeginInvoke(new Action(() => progressBar.Value = (int)percentage));
            //lock (lockObject)
            //progressBar.Value = (int)percentage;

            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => progressBar.Value = (int)percentage));
            }
            else
            {
                progressBar.Value = (int)percentage;
            }
        }

        public CancellationToken GetCancellationToken() => cancellationTokenSource.Token;

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            cancellationTokenSource.Cancel();
        }
    }

}
