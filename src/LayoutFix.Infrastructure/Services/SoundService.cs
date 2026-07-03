using LayoutFix.Core.Interfaces;

namespace LayoutFix.Infrastructure.Services;

public class SoundService : ISoundService
{
    private void PlayWav(string fileName)
    {
        try
        {
            string path = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Sounds", fileName);
            if (System.IO.File.Exists(path))
            {
                using var player = new System.Media.SoundPlayer(path);
                player.Play();
            }
        }
        catch { }
    }

    public void PlaySwitchSound() => PlayWav("switch.wav");
    public void PlayAutoConvertSound() => PlayWav("replace.wav");
    public void PlayErrorSound() => PlayWav("misprint.wav");
}
