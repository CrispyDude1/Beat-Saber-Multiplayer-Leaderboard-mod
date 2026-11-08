using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace MultiplayerLeaderboard.Diagnostics
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }

    /// <summary>
    /// File diagnostics for Multiplayer Leaderboard. Entirely self-contained: it writes its own file
    /// (&lt;game&gt;\Logs\MultiplayerLeaderboard\MultiplayerLeaderboard.log) and mirrors into the mod-loader
    /// log, so it never depends on another mod's logging and never writes into Kick Mod's or Ping Mod's
    /// folders. The path is derived from the running game, never from a developer machine path.
    /// </summary>
    public static class LeaderboardLog
    {
        private const long MaxBytes = 2L * 1024L * 1024L;
        private static readonly object Sync = new object();
        private static float _nextFlushAt;

        private static StreamWriter _writer;
        private static Action<LogLevel, string> _mirror;

        public static string DirectoryPath { get; private set; } = string.Empty;
        public static string FilePath { get; private set; } = string.Empty;
        public static bool Available { get; private set; }

        public static void SetMirror(Action<LogLevel, string> mirror)
        {
            _mirror = mirror;
        }

        public static bool Initialize(string directory)
        {
            lock (Sync)
            {
                DirectoryPath = directory;
                FilePath = Path.Combine(directory, "MultiplayerLeaderboard.log");
                try
                {
                    Directory.CreateDirectory(directory);
                    RotateIfNeeded();
                    _writer = new StreamWriter(
                        new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                        new UTF8Encoding(false));
                    _writer.AutoFlush = false;
                    Available = true;
                    return true;
                }
                catch (Exception ex)
                {
                    Available = false;
                    Trace.WriteLine("MultiplayerLeaderboard: could not open log file: " + ex);
                    return false;
                }
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                FileInfo info = new FileInfo(FilePath);
                if (!info.Exists || info.Length < MaxBytes)
                {
                    return;
                }

                string previous = Path.Combine(DirectoryPath, "MultiplayerLeaderboard.previous.log");
                if (File.Exists(previous))
                {
                    File.Delete(previous);
                }

                File.Move(FilePath, previous);
            }
            catch
            {
                // Rotation is best effort; a locked file must never stop the mod from logging.
            }
        }

        public static void FlushIfDue(float now)
        {
            if (now < _nextFlushAt)
            {
                return;
            }

            _nextFlushAt = now + 5f;
            Flush();
        }

        public static void Flush()
        {
            lock (Sync)
            {
                try
                {
                    _writer?.Flush();
                }
                catch
                {
                    // a failed flush must not stall the frame
                }
            }
        }

        public static void Close()
        {
            lock (Sync)
            {
                try
                {
                    _writer?.Flush();
                    _writer?.Dispose();
                }
                catch
                {
                    // ignored
                }

                _writer = null;
                Available = false;
            }
        }

        public static void Debug(string message) => Write(LogLevel.Debug, message);
        public static void Info(string message) => Write(LogLevel.Info, message);
        public static void Warn(string message) => Write(LogLevel.Warn, message);
        public static void Error(string message) => Write(LogLevel.Error, message);

        public static void Error(string message, Exception ex)
        {
            Write(LogLevel.Error, message + Environment.NewLine + ex);
        }

        public static void Write(LogLevel level, string message)
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string line = "[" + stamp + "] [" + Label(level) + "] " + (message ?? string.Empty);

            lock (Sync)
            {
                try
                {
                    _writer?.WriteLine(line);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("MultiplayerLeaderboard: log write failed: " + ex.Message);
                }
            }

            try
            {
                _mirror?.Invoke(level, message);
            }
            catch
            {
                // Mirroring into the loader log must never break the mod.
            }
        }

        public static void Banner(string title)
        {
            // Single line: repeated startup cycles were spending three lines and 144 dash characters each.
            Write(LogLevel.Info, "=== " + title + " ===");
        }

        private static string Label(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Warn: return "WARN";
                case LogLevel.Error: return "ERROR";
                default: return "INFO";
            }
        }
    }
}
