using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Captura.FFmpeg
{
    public enum FFmpegExtractionMethod
    {
        BuiltIn,
        External7Zip
    }

    public static class DownloadFFmpeg
    {
        static readonly Uri FFmpegUri;
        static readonly string FFmpegArchivePath;

        // 7-Zip standalone console executable (7zr.exe) from the developer's site.
        static readonly Uri SevenZipStandaloneUri = new Uri("https://www.7-zip.org/a/7zr.exe");

        static DownloadFFmpeg()
        {
            FFmpegUri = new Uri("https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-full.7z");
            FFmpegArchivePath = Path.Combine(Path.GetTempPath(), "ffmpeg-release-full.7z");
        }

        public static async Task DownloadArchive(Action<int> Progress, IWebProxy Proxy, CancellationToken CancellationToken)
        {
            using var webClient = new WebClient { Proxy = Proxy };
            CancellationToken.Register(() => webClient.CancelAsync());

            webClient.DownloadProgressChanged += (S, E) =>
            {
                Progress?.Invoke(E.ProgressPercentage);
            };

            await webClient.DownloadFileTaskAsync(FFmpegUri, FFmpegArchivePath);
        }

        public static async Task ExtractTo(string FolderPath)
        {
            await ExtractTo(FolderPath, FFmpegExtractionMethod.BuiltIn, null, CancellationToken.None);
        }

        public static async Task ExtractTo(string FolderPath,
            FFmpegExtractionMethod Method,
            IWebProxy Proxy,
            CancellationToken CancellationToken)
        {
            if (Method == FFmpegExtractionMethod.External7Zip)
            {
                await ExtractWith7Zip(FolderPath, Proxy, CancellationToken);
                return;
            }

            await ExtractWithSharpCompress(FolderPath);
        }

        static Task ExtractWithSharpCompress(string FolderPath)
        {
            return Task.Run(() =>
            {
                if (!File.Exists(FFmpegArchivePath))
                    throw new FileNotFoundException("FFmpeg archive not found.");

                Directory.CreateDirectory(FolderPath);

                using var archive = ArchiveFactory.Open(FFmpegArchivePath);

                var ffmpegEntry = archive.Entries
                    .Where(M => !M.IsDirectory)
                    .FirstOrDefault(M =>
                        string.Equals(
                            Path.GetFileName(M.Key),
                            "ffmpeg.exe",
                            StringComparison.OrdinalIgnoreCase));

                if (ffmpegEntry == null)
                    throw new FileNotFoundException("ffmpeg.exe not found inside the downloaded archive.");

                ffmpegEntry.WriteToDirectory(FolderPath, new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });

                MoveExtractedFfmpegToRoot(FolderPath);
            });
        }

        static async Task ExtractWith7Zip(string FolderPath, IWebProxy Proxy, CancellationToken CancellationToken)
        {
            if (!File.Exists(FFmpegArchivePath))
                throw new FileNotFoundException("FFmpeg archive not found.");

            Directory.CreateDirectory(FolderPath);

            var sevenZipPath = GetSevenZipPath(FolderPath);

            if (sevenZipPath == null)
            {
                sevenZipPath = await DownloadSevenZipStandalone(FolderPath, Proxy, CancellationToken);
            }

            await Task.Run(() =>
            {
                RunSevenZip(sevenZipPath, $"x \"{FFmpegArchivePath}\" -o\"{FolderPath}\" -y");

                var ffmpegSource = Directory
                    .EnumerateFiles(FolderPath, "ffmpeg.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(M => !Path.GetFullPath(M).Equals(sevenZipPath, StringComparison.OrdinalIgnoreCase));

                if (ffmpegSource == null)
                    throw new FileNotFoundException("ffmpeg.exe could not be extracted.");

                var outputFile = Path.Combine(FolderPath, "ffmpeg.exe");

                if (!string.Equals(ffmpegSource, outputFile, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(ffmpegSource, outputFile, true);
                }

                if (!File.Exists(outputFile))
                    throw new FileNotFoundException("ffmpeg.exe could not be extracted.");
            }, CancellationToken);
        }

        static string GetSevenZipPath(string FolderPath)
        {
            // Look for 7za.exe or 7z.exe in the target folder or in a "7zip" subfolder.
            var candidates = new[]
            {
                Path.Combine(FolderPath, "7za.exe"),
                Path.Combine(FolderPath, "7z.exe"),
                Path.Combine(FolderPath, "7zr.exe"),
                Path.Combine(FolderPath, "7zip", "7za.exe"),
                Path.Combine(FolderPath, "7zip", "7z.exe"),
                Path.Combine(FolderPath, "7zip", "7zr.exe")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        static async Task<string> DownloadSevenZipStandalone(string FolderPath, IWebProxy Proxy, CancellationToken CancellationToken)
        {
            var sevenZipDir = Path.Combine(FolderPath, "7zip");
            Directory.CreateDirectory(sevenZipDir);

            var sevenZipPath = Path.Combine(sevenZipDir, "7zr.exe");

            using var webClient = new WebClient { Proxy = Proxy };
            CancellationToken.Register(() => webClient.CancelAsync());

            try
            {
                await webClient.DownloadFileTaskAsync(SevenZipStandaloneUri, sevenZipPath);
            }
            catch (WebException)
            {
                throw;
            }

            if (!File.Exists(sevenZipPath))
                throw new FileNotFoundException("7-Zip standalone could not be downloaded.");

            return sevenZipPath;
        }

        static void RunSevenZip(string SevenZipPath, string Arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = SevenZipPath,
                Arguments = Arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(psi);

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                throw new Win32Exception(process.ExitCode, $"7-Zip extraction failed. {error}");
            }
        }

        static void MoveExtractedFfmpegToRoot(string FolderPath)
        {
            var outputFile = Path.Combine(FolderPath, "ffmpeg.exe");

            var extractedFile = Directory
                .EnumerateFiles(FolderPath, "ffmpeg.exe", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (extractedFile == null)
                throw new FileNotFoundException("ffmpeg.exe could not be extracted.");

            if (!string.Equals(extractedFile, outputFile, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(extractedFile, outputFile, true);
            }

            if (!File.Exists(outputFile))
                throw new FileNotFoundException("ffmpeg.exe could not be extracted.");
        }
    }
}
