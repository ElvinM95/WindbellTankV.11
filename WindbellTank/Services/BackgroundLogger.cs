using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace WindbellTank.Services
{
    /// <summary>
    /// Arxa plan loglarını buferə toplayan, ekranda menyunu pozmayan log sistemi.
    /// Loglar yalnız PrintMainMenu() çağırılanda və ya istifadəçi xüsusi olaraq istədikdə göstərilir.
    /// </summary>
    public static class BackgroundLogger
    {
        private static readonly ConcurrentQueue<LogEntry> _logBuffer = new();
        private static readonly object _consoleLock = new();
        private static int _maxBufferSize = 200;
        private static bool _menuActive = true; // true olduqda loglar buferə yazılır

        public class LogEntry
        {
            public DateTime Time { get; set; }
            public string Message { get; set; } = string.Empty;
            public ConsoleColor Color { get; set; }
            public bool IsMultiLine { get; set; }
        }

        /// <summary>
        /// Menyunun aktiv olub-olmaması. true = loglar buferə gedir, false = birbaşa ekrana yazılır.
        /// </summary>
        public static bool MenuActive
        {
            get => _menuActive;
            set => _menuActive = value;
        }

        /// <summary>
        /// Arxa plan logu yaz. Menyu aktivdirsə buferə, deyilsə birbaşa ekrana.
        /// </summary>
        public static void Log(string message, ConsoleColor color = ConsoleColor.White)
        {
            var entry = new LogEntry
            {
                Time = DateTime.Now,
                Message = message,
                Color = color,
                IsMultiLine = false
            };

            if (_menuActive)
            {
                EnqueueLog(entry);
            }
            else
            {
                WriteToConsole(entry);
            }
        }

        /// <summary>
        /// Çox-sətirli arxa plan logu yaz (PrintTankData, UploadAtgData kimi).
        /// </summary>
        public static void LogBlock(string message, ConsoleColor color = ConsoleColor.White)
        {
            var entry = new LogEntry
            {
                Time = DateTime.Now,
                Message = message,
                Color = color,
                IsMultiLine = true
            };

            if (_menuActive)
            {
                EnqueueLog(entry);
            }
            else
            {
                WriteToConsole(entry);
            }
        }

        /// <summary>
        /// Buferə log əlavə et, ölçü limitini keçsə köhnəni sil.
        /// </summary>
        private static void EnqueueLog(LogEntry entry)
        {
            _logBuffer.Enqueue(entry);
            while (_logBuffer.Count > _maxBufferSize)
                _logBuffer.TryDequeue(out _);
        }

        /// <summary>
        /// Birbaşa konsola yaz (menyu aktiv olmadıqda).
        /// </summary>
        private static void WriteToConsole(LogEntry entry)
        {
            lock (_consoleLock)
            {
                Console.ForegroundColor = entry.Color;
                if (entry.IsMultiLine)
                    Console.WriteLine(entry.Message);
                else
                    Console.WriteLine($"[{entry.Time:HH:mm:ss}] {entry.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Buferdəki bütün logları ekrana çap edib buferi təmizlə.
        /// PrintMainMenu()-dən çağırılır.
        /// </summary>
        public static int FlushToConsole()
        {
            int count = 0;
            while (_logBuffer.TryDequeue(out var entry))
            {
                WriteToConsole(entry);
                count++;
            }
            return count;
        }

        /// <summary>
        /// Buferdə neçə gözləyən log var.
        /// </summary>
        public static int PendingCount => _logBuffer.Count;

        /// <summary>
        /// Buferdəki bütün logları string olaraq qaytar (göstərmə üçün).
        /// Buferi təmizləyir.
        /// </summary>
        public static string FlushToString()
        {
            var sb = new StringBuilder();
            while (_logBuffer.TryDequeue(out var entry))
            {
                if (entry.IsMultiLine)
                    sb.AppendLine(entry.Message);
                else
                    sb.AppendLine($"[{entry.Time:HH:mm:ss}] {entry.Message}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Buferi tamamilə təmizlə.
        /// </summary>
        public static void Clear()
        {
            while (_logBuffer.TryDequeue(out _)) { }
        }
    }
}
