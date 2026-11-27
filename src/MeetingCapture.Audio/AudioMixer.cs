using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingCapture.Audio;

public static class AudioMixer
{
    public static async Task MixAudioFilesAsync(string systemWavPath, string micWavPath, string mixedOutputPath)
    {
        await Task.Run(() =>
        {
            bool hasSystem = File.Exists(systemWavPath) && new FileInfo(systemWavPath).Length > 100;
            bool hasMic = File.Exists(micWavPath) && new FileInfo(micWavPath).Length > 100;

            if (!hasSystem && !hasMic) return;

            var dir = Path.GetDirectoryName(mixedOutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // If only one exists, copy or convert directly
            if (hasSystem && !hasMic)
            {
                File.Copy(systemWavPath, mixedOutputPath, true);
                return;
            }
            if (!hasSystem && hasMic)
            {
                File.Copy(micWavPath, mixedOutputPath, true);
                return;
            }

            try
            {
                using var reader1 = new AudioFileReader(systemWavPath);
                using var reader2 = new AudioFileReader(micWavPath);

                var mixer = new MixingSampleProvider(new[] { reader1, reader2 })
                {
                    ReadFully = false
                };

                WaveFileWriter.CreateWaveFile16(mixedOutputPath, mixer);
            }
            catch
            {
                // Fallback: copy primary system audio
                if (hasSystem)
                {
                    File.Copy(systemWavPath, mixedOutputPath, true);
                }
            }
        });
    }
}
