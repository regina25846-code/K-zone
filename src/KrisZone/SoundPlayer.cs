using System;
using System.Windows;

namespace KrisZone
{
    internal static class SoundPlayer
    {
        // PlaySync를 각각 새 스레드에서 띄우면 winmm의 wave-out 디바이스가 재생을 하나씩
        // 직렬화해버려서, 빠르게 연타하면 밀린 소리가 한참 뒤까지 순서대로 계속 흘러나옴.
        // 그래서 재생 인스턴스를 하나만 재사용하면서 새로 틀기 직전에 이전 재생을 Stop()으로
        // 끊어버리는 방식으로 바꿈 — 항상 가장 최근 트리거만 들리고 밀린 건 즉시 끊김.
        private static System.Media.SoundPlayer? _player;
        private static readonly object _lock = new();

        public static void Play(string resourceFileName)
        {
            lock (_lock)
            {
                try
                {
                    _player?.Stop();
                    var uri = new Uri($"pack://application:,,,/Resources/{resourceFileName}");
                    var stream = Application.GetResourceStream(uri)?.Stream;
                    if (stream == null) return;
                    _player = new System.Media.SoundPlayer(stream);
                    _player.Play();
                }
                catch { }
            }
        }
    }
}
