using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBeePlugin
{
    internal class YtDlp
    {
        public string GetFirstLink(string query)
        {
            string command = "yt-dlp";
            string arguments = $"\"ytsearch:{Utils.EscapeQuotes(query)}\" --get-id";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                string[] videoIds = output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (videoIds.Length > 0)
                {
                    string videoId = videoIds[0].Trim();
                    return $"https://www.youtube.com/watch?v={videoId}";
                }
            }

            return null;
        }

        public async Task<string> SearchAndDownload(string searchQuery, string outPathNoExt, bool showWindow=false, Action<string> onPrint=null /*, CancellationToken cancellationToken*/)
        {
            try
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

                //using (var process = new Process())
                //{
                //    process.StartInfo.FileName = "yt-dlp";
                //    process.StartInfo.Arguments = ytDlpCommand;
                //    process.StartInfo.UseShellExecute = false;
                //    process.StartInfo.RedirectStandardOutput = true;
                //    process.StartInfo.RedirectStandardError = true;
                //    process.StartInfo.CreateNoWindow = true;

                //    process.Start();

                //    var outputTask = Task.Run(async () =>
                //    {
                //        while (!process.StandardOutput.EndOfStream)
                //        {
                //            cancellationToken.ThrowIfCancellationRequested();
                //            string line = await process.StandardOutput.ReadLineAsync();
                //            //onPrint.Invoke(line);
                //            Debug.WriteLine(line);
                //        }
                //    });

                //    var errorTask = Task.Run(async () =>
                //    {
                //        string error = await process.StandardError.ReadToEndAsync();
                //        if (process.ExitCode != 0)
                //        {
                //            throw new Exception($"Command failed: {error}");
                //        }
                //    });

                //    //await Task.WhenAll(outputTask, errorTask);

                //    process.WaitForExit();
                //}

                return outPathNoExt + ".opus";
            }
            finally
            {

            }
        }

    }
}
