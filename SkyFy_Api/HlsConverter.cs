using System.Diagnostics;

namespace SkyFy_Api
{

    public static class HlsConverter
    {
        // returns path to the .m3u8 that was created
        public static async Task<string> ConvertMp3ToHlsAsync(string inputMp3Path, string outputFolder, int segmentSeconds = 5)
        {
            Directory.CreateDirectory(outputFolder);

            var playlistPath = Path.Combine(outputFolder, "playlist.m3u8");

            var args =
                $"-y -i \"{inputMp3Path}\" " +
                $"-c:a aac -b:a 192k " +               
                $"-vn -hls_time {segmentSeconds} " +  
                "-hls_playlist_type vod " +          
                "-hls_segment_filename \"" + Path.Combine(outputFolder, "seg%05d.ts") + "\" " +
                $"\"{playlistPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)!;
            string stderr = await proc.StandardError.ReadToEndAsync(); 
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0 || !File.Exists(playlistPath))
                throw new Exception("FFmpeg failed: " + stderr);

            return playlistPath;
        }
    }

}
