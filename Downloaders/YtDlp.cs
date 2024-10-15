using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MusicBeePlugin
{
    internal class YtDlp
    {
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

                return outPathNoExt + ".opus";
            }
            finally
            {

            }
        }

    }
}
