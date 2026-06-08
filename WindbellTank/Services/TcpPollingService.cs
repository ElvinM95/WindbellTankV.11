using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace WindbellTank.Services
{
    // ── Program.cs-dəki köhnə TCP Polling + SQL Server məntiqi ──────────
    // Arxa planda (BackgroundService) işləyir, konsol menyusuna mane olmur.

    public class TcpPollingService
    {
        private readonly CancellationToken _ct;
        private string? _lastAtgDataCache = null;
        private string? _deviceIp = null;
        private readonly int _devicePort = 5656;

        public bool IsRunning { get; private set; }
        public string? CurrentIp => _deviceIp;

        public TcpPollingService(CancellationToken ct)
        {
            _ct = ct;
        }

        // ── Əsas Polling Döngüsü ──────────────────────────────────────
        public async Task RunAsync()
        {
            IsRunning = true;
            BackgroundLogger.Log("TCP Polling arxa planda başladı.", ConsoleColor.DarkCyan);

            while (!_ct.IsCancellationRequested)
            {
                try
                {
                    int tankCount = GetTankCountFromDatabase();

                    if (string.IsNullOrEmpty(_deviceIp))
                    {
                        _deviceIp = GetIpFromDatabase();
                    }

                    if (string.IsNullOrEmpty(_deviceIp))
                    {
                        // IP yoxdursa gözlə — konsol menyusundan daxil edilə bilər
                        BackgroundLogger.Log("[TCP] Bazada IP tapılmadı, 10 saniyə sonra yenidən yoxlanılacaq...", ConsoleColor.DarkYellow);
                        await SafeDelay(10000);
                        continue;
                    }

                    bool connectionSuccess = false;
                    int maxRetries = 3;

                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        if (_ct.IsCancellationRequested) break;

                        try
                        {
                            using (TcpClient client = new TcpClient())
                            {
                                var connectTask = client.ConnectAsync(_deviceIp, _devicePort);
                                if (await Task.WhenAny(connectTask, Task.Delay(5000, _ct)) != connectTask)
                                {
                                    throw new Exception("Bağlantı vaxtı bitdi (Timeout - 5 san).");
                                }

                                if (!client.Connected)
                                    throw new Exception("Bağlantı qurula bilmədi.");

                                using (NetworkStream stream = client.GetStream())
                                {
                                    var tankList = new List<string>();
                                    for (int i = 1; i <= tankCount; i++)
                                        tankList.Add($"\"Tank{i}\"");

                                    string request = $"{{\"tanks\": [{string.Join(", ", tankList)}], \"requestType\": \"status\"}}";
                                    byte[] requestBytes = Encoding.UTF8.GetBytes(request);
                                    await stream.WriteAsync(requestBytes, 0, requestBytes.Length, _ct);

                                    StringBuilder responseBuilder = new StringBuilder();
                                    byte[] buffer = new byte[8192];
                                    AtgResponse? result = null;
                                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                                    while (true)
                                    {
                                        var readTask = stream.ReadAsync(buffer, 0, buffer.Length, _ct);
                                        if (await Task.WhenAny(readTask, Task.Delay(10000, _ct)) != readTask)
                                            throw new Exception("Oxuma Timeout.");

                                        int bytesRead = await readTask;
                                        if (bytesRead == 0)
                                            throw new Exception("Bağlantı kəsildi.");

                                        string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                                        responseBuilder.Append(chunk);

                                        if (responseBuilder.Length > 5 * 1024 * 1024)
                                            throw new Exception("Məlumat həddindən artıq böyükdür.");

                                        string currentResponse = responseBuilder.ToString();
                                        try
                                        {
                                            string trimmed = currentResponse.Replace("\0", "").Trim();
                                            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                                            {
                                                result = JsonSerializer.Deserialize<AtgResponse?>(currentResponse, options);
                                                if (result != null) break;
                                            }
                                        }
                                        catch (JsonException) { }
                                    }

                                    if (result?.data != null && result.data.Count > 0)
                                    {
                                        string currentDataJson = JsonSerializer.Serialize(
                                            result.data.Where(t => t != null).OrderBy(t => t.tank_id));

                                        if (currentDataJson != _lastAtgDataCache)
                                        {
                                            SaveAtgDataToDatabase(result);
                                            _lastAtgDataCache = currentDataJson;
                                            PrintTankData(result, tankCount);
                                        }
                                        else
                                        {
                                            BackgroundLogger.Log("[TCP ℹ] Göstəricilər eynidir, təkrar yazılmadı.", ConsoleColor.DarkGray);
                                        }
                                    }
                                }
                            }
                            connectionSuccess = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            BackgroundLogger.Log($"[TCP] Xəta (Cəhd {attempt}/{maxRetries}): {ex.Message}", ConsoleColor.DarkRed);
                            if (attempt < maxRetries && !_ct.IsCancellationRequested)
                                await SafeDelay(2000);
                        }
                    }

                    if (!connectionSuccess)
                    {
                        BackgroundLogger.Log($"[TCP] Bütün {maxRetries} cəhd uğursuz oldu. 30 san sonra təkrar yoxlanılacaq.", ConsoleColor.DarkRed);
                    }

                    await SafeDelay(30000);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    BackgroundLogger.Log($"[TCP] Gözlənilməz xəta: {ex.Message}", ConsoleColor.DarkRed);
                    await SafeDelay(10000);
                }
            }

            IsRunning = false;
            BackgroundLogger.Log("[TCP] Polling dayandırıldı.", ConsoleColor.DarkYellow);
        }

        // ── IP dəyişdirmə (konsol menyusundan çağırılır) ────────────
        public void SetDeviceIp(string newIp)
        {
            _deviceIp = newIp;
            UpdateIpInDatabase(newIp);
        }

        // ══════════════════════════════════════════════════════════════
        //  KÖHNƏ PROGRAM.CS-DƏN KÖÇÜRÜLƏN SQL/DB METODLARI
        // ══════════════════════════════════════════════════════════════

        private string GetConnectionString()
        {
            string machineName = Environment.MachineName;
            return $"Server={machineName};Database=ofisServer;User Id=sa;Password=374474;Encrypt=False;Connection Timeout=10;";
        }

        private int GetTankCountFromDatabase()
        {
            try
            {
                using var conn = new SqlConnection(GetConnectionString());
                conn.Open();
                // MAX(TankOid) istifadə edirik, COUNT(*) yox.
                // Çünki tank silindikdə (deaktiv) COUNT aşağı düşür,
                // amma cihaz hələ yuxarı nömrəli tankları görür.
                // Məs: TankOid 1,3 olduqda COUNT=2 amma Tank3 üçün 3 lazımdır.
                using var cmd = new SqlCommand("SELECT ISNULL(MAX(TankOid), 0) FROM TankConfig", conn);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value)
                {
                    int maxOid = Convert.ToInt32(res);
                    if (maxOid > 0) return maxOid;
                }
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[TCP ⚠] DB-dən çən sayı oxunarkən xəta: {ex.Message}", ConsoleColor.DarkYellow);
            }
            return 1;
        }

        private string? GetIpFromDatabase()
        {
            try
            {
                using var conn = new SqlConnection(GetConnectionString());
                conn.Open();
                using var cmd = new SqlCommand("SELECT TOP 1 Ip FROM TankConfig WHERE len(isnull(Ip, '')) > 0", conn);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value)
                    return res.ToString()?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[TCP ⚠] DB-dən IP oxunarkən xəta: {ex.Message}", ConsoleColor.DarkYellow);
            }
            return null;
        }

        private void UpdateIpInDatabase(string newIp)
        {
            try
            {
                using var conn = new SqlConnection(GetConnectionString());
                conn.Open();
                string updateSql = "UPDATE TankConfig SET Ip = @Ip";
                using var cmd = new SqlCommand(updateSql, conn);
                cmd.Parameters.AddWithValue("@Ip", newIp);
                int rows = cmd.ExecuteNonQuery();

                if (rows == 0)
                {
                    string insertSql = "INSERT INTO TankConfig (Ip) VALUES (@Ip)";
                    using var insertCmd = new SqlCommand(insertSql, conn);
                    insertCmd.Parameters.AddWithValue("@Ip", newIp);
                    insertCmd.ExecuteNonQuery();
                    BackgroundLogger.Log($"[TCP ✔] Yeni IP bazaya əlavə edildi: {newIp}", ConsoleColor.DarkGreen);
                }
                else
                {
                    BackgroundLogger.Log($"[TCP ✔] IP bazada yeniləndi: {newIp}", ConsoleColor.DarkGreen);
                }
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[TCP ⚠] IP bazaya yazılarkən xəta: {ex.Message}", ConsoleColor.DarkRed);
            }
        }

        private void SaveAtgDataToDatabase(AtgResponse response)
        {
            if (response?.data == null || response.data.Count == 0) return;

            try
            {
                using var conn = new SqlConnection(GetConnectionString());
                conn.Open();

                var existingTanks = new List<int>();
                using (var cmd = new SqlCommand("SELECT TankOid FROM TankConfig", conn))
                {
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                            existingTanks.Add(reader.GetInt32(0));
                    }
                }

                string updateQuery = @"
                    UPDATE TankConfig SET 
                        YanacaqCode = @YanacaqCode, TankFyelName = @TankFyelName,
                        TankCapacity = @TankCapacity, capacity = @TankCapacity, TankLength = @TankLength,
                        CurrentVolume = @CurrentVolume, waterleve = @WaterLevel,
                        temperature = @Temperature, watervolume = @WaterVolume,
                        tcvolume = @TcVolume, ullage = @Ullage,
                        sensorStatus = @SensorStatus, error = @Error,
                        LastUpdate = GETDATE()
                    WHERE TankOid = @TankOid";

                int successCount = 0;
                using var transaction = conn.BeginTransaction();
                try
                {
                    var tanksInResponse = new HashSet<int>();

                    foreach (var tank in response.data)
                    {
                        if (tank == null) continue;
                        tanksInResponse.Add(tank.tank_id);

                        // Requirement: Handle TANK_NOT_FOUND error by logging. HTTP sync logic handles deletion.
                        if (tank.error != null && tank.error.code == "TANK_NOT_FOUND")
                        {
                            BackgroundLogger.Log($"[TCP ⚠] Çən {tank.tank_id} cihazda tapılmadı (TANK_NOT_FOUND). Silinmə HTTP xidmətinə buraxılır.", ConsoleColor.DarkYellow);
                            continue;
                        }

                        if (tank.error != null && (!string.IsNullOrEmpty(tank.error.code) || !string.IsNullOrEmpty(tank.error.message)))
                        {
                            BackgroundLogger.Log($"[TCP ⚠] Çən {tank.tank_id} xəta qaytardı (Kod: {tank.error.code}). Bu çən nəzərə alınmır.", ConsoleColor.DarkYellow);
                            continue;
                        }

                        if (tank.volume == null)
                        {
                            BackgroundLogger.Log($"[TCP ⚠] Çən {tank.tank_id} üçün məlumatlar tam deyil (Həcm yoxdur). Nəzərə alınmır.", ConsoleColor.DarkYellow);
                            continue;
                        }

                        using var cmd = new SqlCommand(updateQuery, conn, transaction);
                        cmd.Parameters.AddWithValue("@TankOid", tank.tank_id);

                        int yanacaqCodeVal = 0;
                        if (!string.IsNullOrEmpty(tank.product_code))
                        {
                            yanacaqCodeVal = tank.product_code.Trim().ToLowerInvariant() switch
                            {
                                "dizel" => 1, "ai-92" => 2, "premium" => 3,
                                "m.qaz" => 4, "super" => 5, "metan" => 6,
                                "propan" => 7, "dizel*" => 8, _ => 0
                            };
                        }

                        cmd.Parameters.AddWithValue("@YanacaqCode", yanacaqCodeVal);
                        cmd.Parameters.AddWithValue("@TankFyelName", (object?)tank.product_code ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@TankCapacity", (object?)tank.capacity ?? 0m);
                        cmd.Parameters.AddWithValue("@TankLength", (object?)tank.oil_level ?? 0m);
                        cmd.Parameters.AddWithValue("@CurrentVolume", (object?)tank.volume ?? 0m);
                        cmd.Parameters.AddWithValue("@WaterLevel", (object?)tank.water_level ?? 0m);
                        cmd.Parameters.AddWithValue("@Temperature", (object?)tank.temperature ?? 0m);
                        cmd.Parameters.AddWithValue("@WaterVolume", (object?)tank.water_volume ?? 0m);
                        cmd.Parameters.AddWithValue("@TcVolume", (object?)tank.tc_volume ?? 0m);
                        cmd.Parameters.AddWithValue("@Ullage", (object?)tank.Ullage ?? 0m);
                        cmd.Parameters.AddWithValue("@SensorStatus", (object?)tank.sensor_status ?? "");

                        string errorDesc = "";
                        if (tank.error != null && (!string.IsNullOrEmpty(tank.error.code) || !string.IsNullOrEmpty(tank.error.message)))
                            errorDesc = $"[{tank.error.code}] {tank.error.message}";
                        cmd.Parameters.AddWithValue("@Error", errorDesc);

                        cmd.ExecuteNonQuery();
                        successCount++;
                    }

                    // Requirement: Log missing tanks (omitted from JSON response) without deleting.
                    foreach (int existingTankOid in existingTanks)
                    {
                        if (!tanksInResponse.Contains(existingTankOid))
                        {
                            BackgroundLogger.Log($"[TCP ⚠] Çən {existingTankOid} cihaz cavabında yoxdur. Silinmə yalnız HTTP xidməti ilə icra olunur.", ConsoleColor.DarkYellow);
                        }
                    }

                    transaction.Commit();
                    
                    BackgroundLogger.Log($"[TCP ✔] {successCount} çən məlumatı bazaya yazıldı (yalnız Update).", ConsoleColor.DarkGreen);
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    BackgroundLogger.Log($"[TCP ⚠] DB yazma xətası (Rollback): {ex.Message}", ConsoleColor.DarkRed);
                }
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[TCP ⚠] DB bağlantı xətası: {ex.Message}", ConsoleColor.DarkRed);
            }
        }

        private void PrintTankData(AtgResponse result, int tankCount)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"\n[TCP] ════════ CİHAZ MƏLUMATLARI ({DateTime.Now:HH:mm:ss}) ════════");
                if (result.metadata != null)
                    sb.AppendLine($"   Vaxt: {result.metadata.timestamp} | Sorğu: {result.metadata.request_id}");

                var receivedTanks = result.data.Where(t => t != null).Select(t => t.tank_id).ToList();
                var missingTanks = Enumerable.Range(1, tankCount).Where(i => !receivedTanks.Contains(i)).ToList();

                if (missingTanks.Count > 0)
                    sb.AppendLine($" [⚠] Gəlməyən çənlər: {string.Join(", ", missingTanks)}");

                foreach (var tank in result.data.Where(t => t != null).OrderBy(t => t.tank_id))
                {
                    if (tank.error != null)
                    {
                        sb.AppendLine($" [Çən {tank.tank_id}] XƏTA: {tank.error.message} (Kod: {tank.error.code})");
                    }
                    else
                    {
                        sb.AppendLine($" [Çən {tank.tank_id}] {tank.product_code ?? "?"} | {tank.sensor_status?.ToUpper() ?? "?"}");
                        sb.AppendLine($"  ► Səviyyə: {tank.oil_level ?? 0} mm | Su: {tank.water_level ?? 0} mm | Ullage: {tank.Ullage ?? 0} mm");
                        sb.AppendLine($"  ► Həcm: {tank.volume ?? 0} L | Tc: {tank.tc_volume ?? 0} L | Su: {tank.water_volume ?? 0} L");
                        sb.AppendLine($"  ► Tutum: {tank.capacity ?? 0} L | Temp: {tank.temperature ?? 0} °C");
                    }
                    sb.AppendLine(new string('-', 55));
                }
                BackgroundLogger.LogBlock(sb.ToString(), ConsoleColor.Cyan);
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[TCP ⚠] Konsol xətası: {ex.Message}", ConsoleColor.DarkYellow);
            }
        }

        // ── Yardımçılar ─────────────────────────────────────────────
        private async Task SafeDelay(int ms)
        {
            try { await Task.Delay(ms, _ct); }
            catch (TaskCanceledException) { }
        }

        // Log metodu artıq BackgroundLogger-ə yönləndirildi.
        // Köhnə metod uyğunluq üçün saxlanılıb.
        private static void Log(string message, ConsoleColor color = ConsoleColor.White)
        {
            BackgroundLogger.Log(message, color);
        }
    }

    // ── Köhnə ATG Model sinifləri (TCP protokolu üçün) ─────────────
    public class ErrorData
    {
        public string code { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
    }

    public class TankData
    {
        public int tank_id { get; set; }
        public string? product_code { get; set; }
        public decimal? oil_level { get; set; }
        public decimal? water_level { get; set; }
        public decimal? temperature { get; set; }
        public decimal? volume { get; set; }
        public decimal? water_volume { get; set; }
        public decimal? tc_volume { get; set; }
        public decimal? capacity { get; set; }
        public decimal? Ullage { get; set; }
        public string? sensor_status { get; set; }
        public ErrorData? error { get; set; }
    }

    public class AtgMetadata
    {
        public string request_id { get; set; } = string.Empty;
        public string timestamp { get; set; } = string.Empty;
    }

    public class AtgResponse
    {
        public bool success { get; set; }
        public AtgMetadata? metadata { get; set; }
        public List<TankData> data { get; set; } = new();
    }
}
