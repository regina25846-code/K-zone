using System;
using System.Threading.Tasks;
using System.Windows;

namespace KrisZone
{
    internal static class SoundPlayer
    {
        // Play()(비동기)는 재생 스레드가 끝나기 전에 player/stream이 GC되면 중간에 끊길 수 있어서,
        // 백그라운드 스레드에서 PlaySync()로 명시적으로 끝까지 들고 있는 방식 사용
        public static void Play(string resourceFileName)
        {
            Task.Run(() =>
            {
                try
                {
                    var uri = new Uri($"pack://application:,,,/Resources/{resourceFileName}");
                    var stream = Application.GetResourceStream(uri)?.Stream;
                    if (stream == null) return;
                    using var player = new System.Media.SoundPlayer(stream);
                    player.PlaySync();
                }
                catch { }
            });
        }
    }
}
