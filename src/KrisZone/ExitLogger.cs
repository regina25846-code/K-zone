using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace KrisZone
{
    // 종료 사유 기록기 (2026-09-04 신설, PND-0033).
    //
    // 배경: 기존 crash.log는 '예외가 났을 때만' 기록해서, 앱이 정상 종료 경로로 조용히 사라지면
    // 아무 흔적도 안 남았다. 2026-09-04 사고에서 crash.log·윈도우 이벤트로그·백신 기록·안정성
    // 모니터 네 곳이 전부 0건이라 원인 추적이 통째로 막혔다. 그래서 '어떤 경로로 종료됐는지'를
    // 정상 종료까지 포함해 한 줄씩 남긴다.
    //
    // 핵심 아이디어 — '종료 기록이 없는 것' 자체가 증거다:
    //   실행 중에는 session.lock(진행 중 표식)을 만들어 두고, 어떤 경로로든 종료 처리를 타면 지운다.
    //   외부에서 강제로 죽이면(TerminateProcess/작업관리자 강제종료/전원차단) 이 표식이 남는다.
    //   다음 실행 때 남아있는 표식을 발견하면 "직전 실행이 종료 기록 없이 사라졌다"고 기록한다.
    //   → 재발 시 exit.log 파일 하나만 보면 '스스로 끝냈나 / 밖에서 죽었나'가 바로 갈린다.
    internal static class ExitLogger
    {
        private static readonly object Gate = new();

        // 종료 줄을 이미 썼는지(중복 방지). OnExit과 ProcessExit이 둘 다 불릴 수 있어서 필요.
        private static bool _sessionClosed;
        private static bool _sessionOpen;
        // session.lock을 이 프로세스가 만들었는지. 중복 실행으로 스스로 물러나는 인스턴스가
        // 먼저 떠 있던 인스턴스의 표식을 지워버리면 안 되므로 반드시 확인한다.
        private static bool _markerOwned;

        private const long MaxBytes = 256 * 1024;  // 이 크기를 넘으면 회전
        private const int KeepLines = 400;         // 회전 시 마지막 N줄만 남김

        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K-Zone");

        // crash.log와 같은 폴더(%APPDATA%\K-Zone)에 둔다 — 문제 생겼을 때 한 곳만 보면 되도록.
        private static string LogPath => Path.Combine(Dir, "exit.log");
        private static string MarkerPath => Path.Combine(Dir, "session.lock");

        public static string LogFilePath => LogPath;

        // ── 공개 API ─────────────────────────────────────────────────────────

        /// <summary>종료로 이어지지 않는 사건(경로 통과 기록)을 한 줄 남긴다.</summary>
        public static void Log(string route, string actor, string? detail = null)
            => Write(Format(route, actor, detail));

        /// <summary>실행 시작. 로그 회전 → 직전 실행의 뒷정리 확인 → START 기록 → 표식 생성.</summary>
        public static void BeginSession()
        {
            lock (Gate)
            {
                if (_sessionOpen) return;
                _sessionOpen = true;
            }

            Rotate();
            CheckPreviousRun();
            Write(Format("START", "앱 내부", $"v{GetVersion()} 실행 시작"));
            WriteMarker();
        }

        /// <summary>
        /// 중복 실행이라 스스로 물러나는 경우. 표식을 건드리면 안 되므로 세션을 '이미 닫힘'으로
        /// 표시해서, 뒤이어 호출되는 EndSession이 먼저 떠 있는 인스턴스의 표식을 지우지 못하게 한다.
        /// </summary>
        public static void MarkDuplicateInstance()
        {
            Write(Format("SINGLE_INSTANCE_DUPLICATE", "앱 내부",
                "이미 실행 중인 K-Zone이 있어 나중에 뜬 이 인스턴스가 스스로 종료함(먼저 뜬 쪽은 그대로 유지)"));
            lock (Gate) { _sessionClosed = true; }
        }

        /// <summary>최종 종료 한 줄. 여러 번 불려도 처음 한 번만 기록된다.</summary>
        public static void EndSession(string route, string actor, string? detail = null)
        {
            lock (Gate)
            {
                if (_sessionClosed) return;
                _sessionClosed = true;
            }
            Write(Format(route, actor, detail));
            DeleteMarker();
        }

        // ── 내부 구현 ────────────────────────────────────────────────────────

        private static string Format(string route, string actor, string? detail)
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.Append(" | pid=").Append(Environment.ProcessId);
            sb.Append(" | ").Append(route);
            sb.Append(" | ").Append(actor);
            if (!string.IsNullOrEmpty(detail)) sb.Append(" | ").Append(detail);
            return sb.ToString();
        }

        private static void Write(string line)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    // WriteThrough + Flush(true): ProcessExit처럼 '마지막 순간'에 쓰는 경로에서
                    // 버퍼에만 남고 디스크에 안 써지는 일이 없도록 강제로 내려쓴다.
                    // FileShare.ReadWrite: 앱이 도는 중에도 메모장으로 열어볼 수 있게.
                    using var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write,
                        FileShare.ReadWrite, 4096, FileOptions.WriteThrough);
                    var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
                catch { }
            }
        }

        private static void Rotate()
        {
            try
            {
                var fi = new FileInfo(LogPath);
                if (!fi.Exists || fi.Length <= MaxBytes) return;
                var lines = File.ReadAllLines(LogPath);
                var keep = lines.Length > KeepLines
                    ? lines[^KeepLines..]
                    : lines;
                File.WriteAllLines(LogPath, keep, new UTF8Encoding(false));
            }
            catch { }
        }

        private static void WriteMarker()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                using var p = Process.GetCurrentProcess();
                // pid만으로는 부족하다 — 윈도우가 pid를 재사용하면 죽은 프로세스를 '살아있다'고
                // 오판할 수 있어서 프로세스 시작시각까지 같이 적어 대조한다.
                File.WriteAllText(MarkerPath,
                    $"{Environment.ProcessId}|{p.StartTime.Ticks}|{DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    new UTF8Encoding(false));
                _markerOwned = true;
            }
            catch { }
        }

        private static void DeleteMarker()
        {
            if (!_markerOwned) return;
            try
            {
                if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
                _markerOwned = false;
            }
            catch { }
        }

        private static void CheckPreviousRun()
        {
            try
            {
                if (!File.Exists(MarkerPath)) return;

                var raw = File.ReadAllText(MarkerPath).Trim();
                var parts = raw.Split('|');
                var startedAt = parts.Length >= 3 ? parts[2] : "시각 불명";

                if (parts.Length >= 2
                    && int.TryParse(parts[0], out var oldPid)
                    && long.TryParse(parts[1], out var oldTicks)
                    && IsStillRunning(oldPid, oldTicks))
                {
                    // 정상적으로는 중복실행 방지에서 걸러지므로 여기 오면 이례적인 상황이다.
                    Write(Format("PREVIOUS_RUN_STILL_ALIVE", "앱 내부",
                        $"직전 인스턴스(pid={oldPid})가 아직 살아있음 — 표식은 그대로 둔다"));
                    return;
                }

                Write(Format("PREVIOUS_RUN_NO_EXIT_RECORD", "알 수 없음",
                    $"직전 실행({startedAt} 시작, pid={parts[0]})이 종료 기록을 남기지 못하고 사라졌다 " +
                    "— 외부 강제종료·전원차단·커널 정지 의심"));
                File.Delete(MarkerPath);
            }
            catch { }
        }

        private static bool IsStillRunning(int pid, long startTicks)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return p.StartTime.Ticks == startTicks;
            }
            catch { return false; }
        }

        private static string GetVersion()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                return v?.Split('+')[0] ?? "?";
            }
            catch { return "?"; }
        }
    }
}
