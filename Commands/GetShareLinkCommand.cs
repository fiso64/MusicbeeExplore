using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using static MusicBeePlugin.Plugin;
using MusicBeePlugin.Services;
using MusicBeePlugin.Downloaders;
using System.IO;
using System.Net.Http;
using System.Diagnostics;

namespace MusicBeePlugin.Commands
{
    public class GetShareLinkCommand : ICommand
    {
        private readonly YtDlp _ytDlp;

        public GetShareLinkCommand()
        {
            _ytDlp = new YtDlp();
        }

        public async Task Execute()
        {
            var x = MusicBeeHelpers.GetFirstSelected();
            string searchQuery = $"{DummyProcessor.RemoveIdentifier(x.artist)} - {x.title}";

            var form = new Form
            {
                Text = "Share Link",
                Size = new Size(250, 250),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.Manual,
                KeyPreview = true,
            };

            WinApiHelpers.CenterForm(form, mbApi.MB_GetWindowHandle());

            form.KeyDown += (s, e) => 
            {
                if (e.KeyCode == Keys.Escape)
                    form.Close();
            };

            var loadingPanel = new Panel
            {
                Size = new Size(80, 80),
                Location = new Point((form.ClientSize.Width - 80) / 2, (form.ClientSize.Height - 80) / 2)
            };
            form.Controls.Add(loadingPanel);

            var loadingTimer = new Timer { Interval = 15 };
            int angle = 0;
            loadingTimer.Tick += (s, e) =>
            {
                angle = (angle + 5) % 360;
                loadingPanel.Invalidate();
            };

            loadingPanel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(0, 120, 215), 4))
                {
                    var rect = new Rectangle(10, 10, 60, 60);
                    e.Graphics.DrawArc(pen, rect, angle, 90);
                }
            };

            loadingTimer.Start();
            form.Show();

            try
            {
                var videoInfo = await _ytDlp.GetVideoInfo(searchQuery);
                UpdateFormWithVideoInfo(form, videoInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                form.Close();
            }
            finally
            {
                loadingTimer.Stop();
            }
        }

        private void UpdateFormWithVideoInfo(Form form, YoutubeVideoInfo videoInfo)
        {
            form.Invoke((MethodInvoker)delegate
            {
                form.Controls.Clear();
                form.Size = new Size(360, 380);

                WinApiHelpers.CenterForm(form, mbApi.MB_GetWindowHandle());

                Font modernFont = new Font("Segoe UI", 10F);
                form.Font = modernFont;

                PictureBox thumbnailBox = new PictureBox
                {
                    Size = new Size(320, 180),
                    Location = new Point((form.ClientSize.Width - 320) / 2, 15),
                    SizeMode = PictureBoxSizeMode.Zoom
                };
                form.Controls.Add(thumbnailBox);

                Label titleLabel = new Label
                {
                    Text = videoInfo.Title,
                    AutoSize = false,
                    Size = new Size(320, 20),
                    Location = new Point(15, thumbnailBox.Bottom + 10),
                    Font = new Font(modernFont, FontStyle.Bold),
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                form.Controls.Add(titleLabel);

                Label channelLabel = new Label
                {
                    Text = videoInfo.Channel,
                    AutoSize = true,
                    Location = new Point(15, titleLabel.Bottom + 5),
                    ForeColor = Color.DarkGray
                };
                form.Controls.Add(channelLabel);

                TextBox urlTextBox = new TextBox
                {
                    Text = videoInfo.WebpageUrl,
                    Size = new Size(320, 25),
                    Location = new Point(15, channelLabel.Bottom + 15),
                    ReadOnly = true,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.WhiteSmoke
                };
                form.Controls.Add(urlTextBox);

                urlTextBox.KeyDown += (sender, e) =>
                {
                    if (e.Control && e.KeyCode == Keys.A)
                    {
                        urlTextBox.SelectAll();
                        e.SuppressKeyPress = true;
                    }
                };

                Button playAudioButton = new Button
                {
                    Text = "Play audio",
                    Size = new Size(100, 25),
                    Location = new Point(15, urlTextBox.Bottom + 15),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 120, 215),
                    ForeColor = Color.White,
                    Font = new Font(modernFont.FontFamily, 9f)
                };
                playAudioButton.Click += (sender, e) => Utils.PlayWithMediaPlayer(videoInfo.WebpageUrl, config.MediaPlayerCommand);
                form.Controls.Add(playAudioButton);

                Button playVideoButton = new Button
                {
                    Text = "Play video",
                    Size = new Size(100, 25),
                    Location = new Point(playAudioButton.Right + 9, playAudioButton.Top),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 120, 215),
                    ForeColor = Color.White,
                    Font = new Font(modernFont.FontFamily, 9f)
                };
                playVideoButton.Click += (sender, e) => 
                {
                    string videoCommand = config.MediaPlayerCommand.Replace("--no-video", "");
                    Utils.PlayWithMediaPlayer(videoInfo.WebpageUrl, videoCommand);
                };
                form.Controls.Add(playVideoButton);

                Button openBrowserButton = new Button
                {
                    Text = ">> YouTube",
                    Size = new Size(100, 25),
                    Location = new Point(playVideoButton.Right + 9, playVideoButton.Top),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 120, 215),
                    ForeColor = Color.White,
                    Font = new Font(modernFont.FontFamily, 9f)
                };
                openBrowserButton.Click += (sender, e) => System.Diagnostics.Process.Start(videoInfo.WebpageUrl);
                form.Controls.Add(openBrowserButton);

                urlTextBox.SelectAll();
                urlTextBox.Focus();

                Task.Run(() => ProcessThumbnail(videoInfo.Thumbnail, thumbnailBox));
            });
        }

        private async Task ProcessThumbnail(string thumbnailUrl, PictureBox thumbnailBox)
        {
            try
            {
                string tempDir = Path.GetTempPath();
                string originalFile = Path.Combine(tempDir, "thumbnail_original.webp");
                string convertedFile = Path.Combine(tempDir, "thumbnail_converted.jpg");

                using (var client = new HttpClient())
                {
                    byte[] imageData = await client.GetByteArrayAsync(thumbnailUrl);
                    File.WriteAllBytes(originalFile, imageData);
                }

                await ConvertImageWithFFmpeg(originalFile, convertedFile);

                thumbnailBox.Invoke((MethodInvoker)delegate
                {
                    using (var stream = new FileStream(convertedFile, FileMode.Open, FileAccess.Read))
                    {
                        thumbnailBox.Image = Image.FromStream(stream);
                    }
                });

                File.Delete(originalFile);
                File.Delete(convertedFile);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing thumbnail: {ex.Message}");
            }
        }

        private async Task ConvertImageWithFFmpeg(string inputFile, string outputFile)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{inputFile}\" \"{outputFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"FFmpeg conversion failed with exit code {process.ExitCode}");
                }
            }
        }
    }
}
