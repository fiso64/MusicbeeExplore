using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Net.Http;

namespace MusicBeePlugin.Downloaders
{
    internal class YtDlp
    {
        public async Task<string> GetAudioUrl(string searchQuery)
        {
            string ytDlpCommand = $"\"ytsearch:{Utils.EscapeQuotes(searchQuery)}\" -x --get-url";

            Process process = new Process();
            process.StartInfo.FileName = "yt-dlp";
            process.StartInfo.Arguments = ytDlpCommand;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"yt-dlp process exited with code {process.ExitCode}");
            }

            return output;
        }

        public async Task<string> SearchAndDownload(string searchQuery, string outPathNoExt, bool showWindow=false, Action<string> onPrint=null /*, CancellationToken cancellationToken*/)
        {
            return await Task.Run(() => SearchAndDownloadInternal(searchQuery, outPathNoExt, showWindow, onPrint));
        }

        private string SearchAndDownloadInternal(string searchQuery, string outPathNoExt, bool showWindow = false, Action<string> onPrint = null)
        {
            string ytDlpCommand = $"\"ytsearch:{Utils.EscapeQuotes(searchQuery)}\" --max-filesize 200M -x --audio-format opus -o \"{outPathNoExt}\"";

            Debug.WriteLine(ytDlpCommand);

            Process process = new Process();
            process.StartInfo.FileName = "yt-dlp";
            process.StartInfo.Arguments = ytDlpCommand;
            process.StartInfo.RedirectStandardOutput = !showWindow;
            process.StartInfo.UseShellExecute = showWindow;
            process.StartInfo.CreateNoWindow = !showWindow;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    Debug.WriteLine(args.Data);
                    if (onPrint != null)
                        onPrint(args.Data);
                }
            };

            var mainWindowHandle = WinApiHelpers.GetForegroundWindow();

            process.Start();

            if (!showWindow)
                process.BeginOutputReadLine();

            if (showWindow)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(100);
                    WinApiHelpers.SetForegroundWindow(mainWindowHandle);
                });
            }

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"yt-dlp process exited with code {process.ExitCode}");
            }

            process.Close();

            return outPathNoExt + ".opus";
        }

        public async Task<YoutubeVideoInfo> GetVideoInfo(string searchQuery)
        {
            string ytDlpCommand = $"\"ytsearch:{Utils.EscapeQuotes(searchQuery)}\" --dump-json";

            Process process = new Process();
            process.StartInfo.FileName = "yt-dlp";
            process.StartInfo.Arguments = ytDlpCommand;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"yt-dlp process exited with code {process.ExitCode}");
            }

            return JsonConvert.DeserializeObject<YoutubeVideoInfo>(output);
        }
    }

    public class YoutubeVideoInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("thumbnail")]
        public string Thumbnail { get; set; }

        [JsonProperty("channel")]
        public string Channel { get; set; }

        [JsonProperty("webpage_url")]
        public string WebpageUrl { get; set; }
    }
}
