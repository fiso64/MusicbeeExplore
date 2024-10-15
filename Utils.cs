using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

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

        public static string UnHtmlString(this string s)
        {
            s = WebUtility.HtmlDecode(s);
            string[] zeroWidthChars = { "\u200B", "\u200C", "\u200D", "\u00AD", "\u200E", "\u200F" };
            foreach (var zwChar in zeroWidthChars)
                s = s.Replace(zwChar, "");

            s = s.Replace('\u00A0', ' ');
            return s;
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

        public static void PlayWithMediaPlayer(string url, string command, bool wait = false)
        {
            command = command.Replace("{url}", '\"' + Utils.EscapeQuotes(url.Trim().Trim('"')) + '\"');

            var (filename, arguments) = SplitCommand(command);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = filename,
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    if (wait) process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while trying to play the video: {ex.Message}");
            }
        }

        public static (string filename, string arguments) SplitCommand(string fullCommand)
        {
            fullCommand = fullCommand.Trim();
            
            if (fullCommand.StartsWith("\""))
            {
                int endQuoteIndex = fullCommand.IndexOf("\"", 1);
                if (endQuoteIndex != -1)
                {
                    string command = fullCommand.Substring(1, endQuoteIndex - 1);
                    string arguments = fullCommand.Substring(endQuoteIndex + 1).Trim();
                    return (command, arguments);
                }
            }

            int spaceIndex = fullCommand.IndexOf(' ');
            if (spaceIndex == -1)
            {
                return (fullCommand, "");
            }

            string cmd = fullCommand.Substring(0, spaceIndex);
            string args = fullCommand.Substring(spaceIndex + 1).Trim();
            return (cmd, args);
        }
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
            this.FormClosing += OnFormClosing;
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
        public void UpdateTitle(string title)
        {
            if (cancellationTokenSource.IsCancellationRequested)
                return;

            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => this.Text = title));
            }
            else
            {
                this.Text = title;
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
