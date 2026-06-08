using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using WindbellTank.Models;
using WindbellTank.Services;

namespace WindbellTank
{
    class Program
    {
        private static readonly CancellationTokenSource _cts = new();
        private static SettingsManager _settings = null!;
        private static TcpPollingService _polling = null!;

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; _cts.Cancel(); };

            // ── Connection String (ofisServer bazası — TCP polling ilə eyni) ──
            string machineName = Environment.MachineName;
            string connectionString = $"Server={machineName};Database=ofisServer;User Id=sa;Password=374474;Encrypt=False;Connection Timeout=10;";

            // ── 1. ASP.NET Core API Server (arxa plan) ───────────────
            var builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders(); // ASP.NET Core default loglarını söndürürük
            builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(8080));

            // ── EF Core DbContext qeydiyyatı ─────────────────────────
            builder.Services.AddDbContext<WindbellDbContext>(options =>
                options.UseSqlServer(connectionString));

            // ── DeviceSettingsStore (IServiceScopeFactory ilə) ───────
            builder.Services.AddSingleton<DeviceSettingsStore>(sp =>
                new DeviceSettingsStore(sp.GetRequiredService<IServiceScopeFactory>()));

            // ── API Client və Sync Service ───────────────────────────
            builder.Services.AddHttpClient<WindbellApiClient>(client =>
            {
                client.BaseAddress = new Uri("http://10.40.7.35:8080");
            }).AddTypedClient<WindbellApiClient>((http, sp) => new WindbellApiClient(http, sp.GetRequiredService<DeviceSettingsStore>()));

            builder.Services.AddScoped<DeviceSyncService>();

            builder.Services.AddControllers();
            var app = builder.Build();

            // ── EF Core Migration — cədvəllər avtomatik yaradılır ────
            Log("Verilənlər bazası miqrasiyası işə salınır...", ConsoleColor.Yellow);
            try
            {
                using (var scope = app.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<WindbellDbContext>();
                    db.Database.Migrate();
                }
                Log("DB Miqrasiya uğurla tamamlandı.", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                Log($"DB Miqrasiya xətası: {ex.Message}", ConsoleColor.Red);
                Log("Proqram davam edir, lakin bazada cədvəllər olmaya bilər.", ConsoleColor.Yellow);
            }

            app.Use(async (ctx, next) =>
            {
                BackgroundLogger.Log($"[HTTP] {ctx.Request.Method} {ctx.Request.Path}", ConsoleColor.DarkGray);
                await next.Invoke();
            });
            app.MapControllers();

            var webTask = app.RunAsync(_cts.Token);
            BackgroundLogger.MenuActive = false; // ilk başlanma logları birbaşa göstərilsin
            Log("API Server 0.0.0.0:8080 portunda işə düşdü.", ConsoleColor.Green);

            // ── 2. Servisləri al və Sinxronizasiya Et ───────────────
            var store = app.Services.GetRequiredService<DeviceSettingsStore>();
            store.LoadFromDatabase(); // SQL-dən ayarları yaddaşa yüklə

            using (var scope = app.Services.CreateScope())
            {
                var syncService = scope.ServiceProvider.GetRequiredService<DeviceSyncService>();
                _ = syncService.SyncAllSettingsToDeviceAsync(); // Startup Push (Fire and Forget)
            }

            _settings = new SettingsManager(store);
            _polling = new TcpPollingService(_cts.Token);

            // ── 3. TCP Polling (arxa plan) ──────────────────────────
            var pollingTask = Task.Run(() => _polling.RunAsync());
            Log("TCP Polling arxa planda başladı.", ConsoleColor.Green);

            // ── 4. Konsol Menyusu (ön plan) ─────────────────────
            BackgroundLogger.MenuActive = true; // menyu başlayır, loglar buferə getsin
            await RunMenuLoop();

            _cts.Cancel();
            try { await webTask; } catch { }
            Log("Proqram dayandırıldı.", ConsoleColor.White);
        }

        static async Task RunMenuLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                PrintMainMenu();
                string? input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input)) continue;

                try
                {
                    switch (input)
                    {
                        case "1": await TankMenuAsync(); break;
                        case "2": await ProbeMenuAsync(); break;
                        case "3": HeartbeatInfo(); break;
                        case "4": await TankTableMenuAsync(); break;
                        case "5": await SensorMenuAsync(); break;
                        case "6": await OilProductMenuAsync(); break;
                        case "7": await DensityMenuAsync(); break;
                        case "8": await GasSensorMenuAsync(); break;
                        case "9": _settings.PrintAllSettings(); break;
                        case "10": TcpIpMenu(); break;
                        case "11": await _settings.AddSampleSettingsAsync(); break;
                        case "12": ShowBackgroundLogs(); break;
                        case "0": _cts.Cancel(); return;
                        default: Log("Yanlış seçim!", ConsoleColor.Red); break;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Xəta: {ex.Message}", ConsoleColor.Red);
                }

                if (!_cts.Token.IsCancellationRequested)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("\n  Davam etmək üçün ENTER basın...");
                    Console.ResetColor();
                    Console.ReadLine();
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  MENYU GÖRÜNÜŞLƏRİ
        // ══════════════════════════════════════════════════════════════

        static void PrintMainMenu()
        {
            Console.Clear();
            int pending = BackgroundLogger.PendingCount;
            string logBadge = pending > 0 ? $" 🟢 ({pending} yeni log)" : "";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
  ╔═══════════════════════════════════════════════════════════╗
  ║     WINDBELL SS SERIES ATG — İDARƏETMƏ PANELİ  v4.0     ║
  ╠═══════════════════════════════════════════════════════════╣
  ║                                                           ║
  ║   1.  Tank Ayarları          (uploadTankData)              ║
  ║   2.  Prob Konfiqurasiyası   (uploadProbeData)             ║
  ║   3.  Vaxt Sinxronizasiyası  (Heartbeat Status)            ║
  ║   4.  Tank Cədvəli           (uploadTankVolData)           ║
  ║   5.  Sızıntı Sensoru        (uploadSensorSetData)         ║
  ║   6.  Yağ Məhsulu Ayarları   (uploadOilData)               ║
  ║   7.  Sıxlıq Ayarları        (uploadDensityData)           ║
  ║   8.  Qaz Sensoru Ayarları   (uploadGasSetData)            ║
  ║   9.  Bütün Ayarları Göstər                                ║
  ║  10.  TCP/IP Ayarları                                      ║
  ║  11.  Nümunə Ayarları Yüklə                               ║
  ║  12.  Arxa Plan Logları 📝                                  ║
  ║   0.  Çıxış                                                ║
  ║                                                           ║
  ╚═══════════════════════════════════════════════════════════╝");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [TCP: {(_polling.IsRunning ? "İşləyir" : "Dayanıb")}] [IP: {_polling.CurrentIp ?? "yoxdur"}] [Server: 8080]");
            Console.ResetColor();

            if (pending > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"  ⚠ {pending} arxa plan logu gözləyir (12 basın görmək üçün)");
                Console.ResetColor();
            }

            Console.Write("\n  Seçiminiz: ");
        }

        // ── 1. TANK MENYUSU ────────────────────────────────────────
        static async Task TankMenuAsync()
        {
            Console.WriteLine("\n  [a] Yeni tank əlavə et / redaktə et");
            Console.WriteLine("  [b] Tankı aktiv/deaktiv et");
            Console.WriteLine("  [c] Tankları göstər");
            Console.Write("  Seçim: ");
            string? ch = Console.ReadLine()?.Trim().ToLower();

            switch (ch)
            {
                case "a":
                    string tNo = Ask("Tank nömrəsi (01-12)");
                    string oCode = Ask("Yağ kodu (məs: 1020)");
                    string oName = Ask("Yağ adı (məs: 92#)");
                    int diam = AskInt("Diametr (mm)");
                    int vol = AskInt("Həcm (litr)");
                    string rate = Ask("Genişlənmə əmsalı (məs: 0.0012)", "0.0012");
                    bool en = AskBool("Aktiv? (1=bəli, 0=xeyr)", true);
                    await _settings.SetTankAsync(tNo, oCode, oName, diam, vol, rate, en);
                    break;
                case "b":
                    string tNo2 = Ask("Tank nömrəsi");
                    bool en2 = AskBool("Aktiv et? (1=bəli, 0=xeyr)", true);
                    await _settings.ToggleTankAsync(tNo2, en2);
                    break;
                case "c":
                    _settings.ListTanks();
                    break;
            }
        }

        // ── 2. PROB MENYUSU ─────────────────────────────────────────
        static async Task ProbeMenuAsync()
        {
            Console.WriteLine("\n  [a] Prob əlavə et / redaktə et");
            Console.WriteLine("  [b] Probları göstər");
            Console.Write("  Seçim: ");
            string? ch = Console.ReadLine()?.Trim().ToLower();

            switch (ch)
            {
                case "a":
                    string tNo = Ask("Tank nömrəsi (01-12)");
                    string pId = Ask("Prob ID (6 rəqəm, məs: 100001)");
                    bool isDens = AskBool("Sıxlıq probu? (1=bəli, 0=xeyr)", false);
                    double oilOff = AskDouble("Yanacaq offset (mm)", 0);
                    double watOff = AskDouble("Su offset (mm)", 0);
                    double blind = AskDouble("Yanacaq blind zone (mm)", 50);
                    double hWarn = AskDouble("Yüksək xəbərdarlıq (mm)", 1800);
                    double hAlarm = AskDouble("Yüksək alarm (mm)", 1900);
                    double lWarn = AskDouble("Aşağı xəbərdarlıq (mm)", 200);
                    double lAlarm = AskDouble("Aşağı alarm (mm)", 100);
                    double wWarn = AskDouble("Su xəbərdarlıq (mm)", 30);
                    double wAlarm = AskDouble("Su alarm (mm)", 50);
                    double hTemp = AskDouble("Yüksək temp (°C)", 55);
                    double lTemp = AskDouble("Aşağı temp (°C)", -40);
                    await _settings.SetProbeAsync(tNo, pId, isDens, oilOff, watOff, blind,
                        hWarn, hAlarm, lWarn, lAlarm, wWarn, wAlarm, hTemp, lTemp);
                    break;
                case "b":
                    _settings.ListProbes();
                    break;
            }
        }

        // ── 3. HEARTBEAT / VAXT SİNXRONİZASİYASI ───────────────────
        static void HeartbeatInfo()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($@"
  ════════════════════════════════════════════════════════
   HEARTBEAT / VAXT SİNXRONİZASİYASI
  ════════════════════════════════════════════════════════
   Cihaz hər ~10 saniyədə /deviceAPI/getSysVersionData
   endpoint-inə POST göndərir.
   
   Server avtomatik olaraq cavabda serverTime qaytarır:
   → serverTime = {DateTime.Now:yyyy-MM-dd HH:mm:ss}
   
   Bu, cihazın saatını serverlə sinxronlaşdırır.
   Heç bir əlavə əməliyyat tələb olunmur.
   
   Versiya statusu (cihaz fərq görəndə yeni ayarları çəkir):
   → DeviceApiController.cs → getSysVersionData endpoint
  ════════════════════════════════════════════════════════");
            Console.ResetColor();
        }

        // ── 4. TANK CƏDVƏLİ MENYUSU ────────────────────────────────
        static async Task TankTableMenuAsync()
        {
            Console.WriteLine("\n  [a] Cədvəl yüklə (hündürlük-həcm)");
            Console.WriteLine("  [b] Cədvəli göstər");
            Console.Write("  Seçim: ");
            string? ch = Console.ReadLine()?.Trim().ToLower();

            switch (ch)
            {
                case "a":
                    string tNo = Ask("Tank nömrəsi (01-12)");
                    int count = AskInt("Neçə giriş olacaq?");
                    var entries = new List<(int, int)>();
                    for (int i = 1; i <= count; i++)
                    {
                        Console.Write($"    [{i}] Hündürlük (mm): ");
                        int h = int.Parse(Console.ReadLine()?.Trim() ?? "0");
                        Console.Write($"    [{i}] Həcm (litr): ");
                        int v = int.Parse(Console.ReadLine()?.Trim() ?? "0");
                        entries.Add((h, v));
                    }
                    await _settings.SetTankTableAsync(tNo, entries);
                    break;
                case "b":
                    string tNo2 = Ask("Tank nömrəsi");
                    _settings.ListTankTable(tNo2);
                    break;
            }
        }

        // ── 5. SIZINTI SENSORU MENYUSU ──────────────────────────────
        static async Task SensorMenuAsync()
        {
            Console.WriteLine("\n  [a] Sensor əlavə et / redaktə et");
            Console.WriteLine("  [b] Sensorları göstər");
            Console.Write("  Seçim: ");
            string? ch = Console.ReadLine()?.Trim().ToLower();

            switch (ch)
            {
                case "a":
                    string sNo = Ask("Sensor nömrəsi (01-32)");
                    Console.WriteLine("    Tip: 0=Sızıntı, 1=Digər");
                    string sType = Ask("Sensor tipi", "0");
                    Console.WriteLine("    Mövqe: 0=Ada, 1=Boşaltma, 2=Quyu, 3=Digər");
                    string pos = Ask("Mövqe", "0");
                    string posNum = Ask("Mövqe nömrəsi", "01");
                    bool en = AskBool("Aktiv? (1=bəli, 0=xeyr)", true);
                    await _settings.SetSensorAsync(sNo, sType, pos, posNum, en);
                    break;
                case "b":
                    _settings.ListSensors();
                    break;
            }
        }

        // ── 6. YAĞ MƏHSULU MENYUSU ─────────────────────────────────
        static async Task OilProductMenuAsync()
        {
            Console.WriteLine("\n  [a] Yağ məhsulu əlavə et / redaktə et");
            Console.WriteLine("  [b] Yağ məhsullarını göstər");
            Console.Write("  Seçim: ");
            string? ch = Console.ReadLine()?.Trim().ToLower();

            switch (ch)
            {
                case "a":
                    string oCode = Ask("Yağ kodu (məs: 1020)");
                    string oName = Ask("Yağ adı (məs: 92#)");
                    string color = Ask("Rəng (red/green/blue)", "green");
                    string rate = Ask("Genişlənmə əmsalı (benzin:0.0012, dizel:0.0008)", "0.0012");
                    string temp = Ask("Hesablama temperaturu", "20");
                    string dens = Ask("Çəki sıxlığı", "0.725");
                    await _settings.SetOilProductAsync(oCode, oName, color, rate, temp, dens);
                    break;
                case "b":
                    _settings.ListOilProducts();
                    break;
            }
        }

        // ── 7. SIXLIQ MENYUSU ──────────────────────────────────────
        static async Task DensityMenuAsync()
        {
            Console.WriteLine("\n  [a] Sıxlıq ayarı əlavə et / redaktə et");
            Console.WriteLine("  [b] Sıxlıq ayarlarını göstər");
            Console.Write("  Seçim: ");
            string? ch = Console.ReadLine()?.Trim().ToLower();

            switch (ch)
            {
                case "a":
                    string tNo = Ask("Tank nömrəsi (01-12)");
                    string hDiff = Ask("Hündürlük fərqi", "0");
                    string fRate = Ask("Korreksiya əmsalı", "1.0");
                    string iDens = Ask("Başlanğıc sıxlıq", "0.725");
                    string sDens = Ask("İkinci sıxlıq", "0.720");
                    string fNo = Ask("Float nömrəsi", "1");
                    await _settings.SetDensityAsync(tNo, hDiff, fRate, iDens, sDens, fNo);
                    break;
                case "b":
                    _settings.ListDensities();
                    break;
            }
        }

        // ── 8. QAZ SENSORU MENYUSU ──────────────────────────────────
        static async Task GasSensorMenuAsync()
        {
            Console.WriteLine("\n  [a] Qaz sensoru əlavə et / redaktə et");
            Console.WriteLine("  [b] Qaz sensorlarını göstər");
            Console.Write("  Seçim: ");
            string? ch = Console.ReadLine()?.Trim().ToLower();

            switch (ch)
            {
                case "a":
                    string sNo = Ask("Sensor nömrəsi (01-64)");
                    Console.WriteLine("    Mövqe: 0=Ada, 1=Boşaltma, 2=Quyu, 3=Digər");
                    string pos = Ask("Mövqe", "0");
                    string posNum = Ask("Mövqe nömrəsi", "01");
                    bool en = AskBool("Aktiv? (1=bəli, 0=xeyr)", true);
                    await _settings.SetGasSensorAsync(sNo, pos, posNum, en);
                    break;
                case "b":
                    _settings.ListGasSensors();
                    break;
            }
        }

        // ── 10. TCP/IP MENYUSU ─────────────────────────────────────
        static void TcpIpMenu()
        {
            Console.WriteLine($"\n  Hazırki IP: {_polling.CurrentIp ?? "təyin edilməyib"}");
            Console.WriteLine($"  TCP Status: {(_polling.IsRunning ? "İşləyir" : "Dayanıb")}");
            Console.Write("\n  Yeni IP daxil edin (boş=dəyişmə): ");
            string? newIp = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(newIp))
            {
                _polling.SetDeviceIp(newIp);
                Log($"IP '{newIp}' olaraq dəyişdirildi.", ConsoleColor.Green);
            }
        }

        // ── 12. ARXA PLAN LOGLARI ─────────────────────────────────
        static void ShowBackgroundLogs()
        {
            int count = BackgroundLogger.PendingCount;
            if (count == 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("\n  Heç bir yeni arxa plan logu yoxdur.");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"\n  ════ ARXA PLAN LOGLARI ({count} ədəd) ═══════════════════════════");
            Console.ResetColor();

            BackgroundLogger.FlushToConsole();

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  ═══════════════════════════════════════════════════════");
            Console.ResetColor();
        }

        // ══════════════════════════════════════════════════════════════
        //  YARDIMÇI INPUT METODLARI
        // ══════════════════════════════════════════════════════════════

        static string Ask(string label, string? defaultVal = null)
        {
            Console.Write(defaultVal != null ? $"    {label} [{defaultVal}]: " : $"    {label}: ");
            string? val = Console.ReadLine()?.Trim();
            return string.IsNullOrEmpty(val) ? (defaultVal ?? "") : val;
        }

        static int AskInt(string label, int defaultVal = 0)
        {
            Console.Write($"    {label} [{defaultVal}]: ");
            string? val = Console.ReadLine()?.Trim();
            return string.IsNullOrEmpty(val) ? defaultVal : int.Parse(val);
        }

        static double AskDouble(string label, double defaultVal = 0)
        {
            Console.Write($"    {label} [{defaultVal}]: ");
            string? val = Console.ReadLine()?.Trim();
            return string.IsNullOrEmpty(val) ? defaultVal : double.Parse(val);
        }

        static bool AskBool(string label, bool defaultVal)
        {
            Console.Write($"    {label} [{(defaultVal ? "1" : "0")}]: ");
            string? val = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(val)) return defaultVal;
            return val == "1" || val.ToLower() == "yes" || val.ToLower() == "bəli";
        }

        static void Log(string msg, ConsoleColor color = ConsoleColor.White)
        {
            // Ön plan menyusundan çağırıldıqda birbaşa ekrana yazılır
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            Console.ResetColor();
        }
    }
}