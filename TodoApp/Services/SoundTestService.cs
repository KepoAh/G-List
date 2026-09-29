using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
namespace TodoApp.Services;
// Independent player: testing audio must never touch the reminder queue or task data.
public sealed class SoundTestService : IDisposable
{
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _limit = new() { Interval = TimeSpan.FromSeconds(10) };
    public bool IsPlaying { get; private set; }
    public event Action<string>? Feedback;
    public SoundTestService()
    {
        _limit.Tick += (_, _) => Stop();
        _player.MediaEnded += (_, _) => Stop();
        _player.MediaFailed += (_, _) => { Stop(); Feedback?.Invoke(Localizer.Text("File suara tidak dapat diputar. Pilih MP3 lain.")); };
    }
    public static string ResolvePath(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path));
    public void Play(string path, double volume)
    {
        Stop();
        var resolved = ResolvePath(path);
        if (!File.Exists(resolved)) throw new FileNotFoundException(Localizer.Text("File suara tidak ditemukan. Pilih MP3 terlebih dahulu."));
        _player.Volume = Math.Clamp(volume, 0, 1);
        _player.Open(new Uri(resolved)); IsPlaying = true; _player.Play(); _limit.Start();
        Feedback?.Invoke(Localizer.Text(volume <= 0 ? "Volume 0%. Naikkan volume untuk mendengar tes." : "Tes suara diputar maksimal 10 detik."));
    }
    public void Stop() { _limit.Stop(); _player.Stop(); _player.Close(); IsPlaying = false; }
    public void Dispose() { Stop(); }
}
