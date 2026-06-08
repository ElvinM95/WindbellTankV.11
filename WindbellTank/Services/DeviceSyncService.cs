using System;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using WindbellTank.Models;
namespace WindbellTank.Services
{
    /// <summary>
    /// Verilənlər bazasından cihazla məlumat sinxronizasiyasını idarə edir.
    /// "Startup Push" və "Write-Through" (anında yeniləmə) funksiyalarını təmin edir.
    /// </summary>
    public class DeviceSyncService
    {
        private readonly WindbellApiClient _apiClient;
        private readonly IServiceScopeFactory _scopeFactory;

        public DeviceSyncService(WindbellApiClient apiClient, IServiceScopeFactory scopeFactory)
        {
            _apiClient = apiClient;
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Proqram başladıqda bütün ayarları cihaza göndərir (Startup Push).
        /// </summary>
        public async Task SyncAllSettingsToDeviceAsync()
        {
            BackgroundLogger.Log("[SYNC] İlk heartbeat gözlənilir (AppId öyrənmək üçün, maks. 30 san)...", ConsoleColor.Cyan);
            
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<DeviceSettingsStore>();

                // İlk heartbeat gəlməsini gözlə — düzgün AppId əldə etmək üçün
                await store.WaitForAppIdAsync();
                BackgroundLogger.Log($"[SYNC] AppId müəyyən edildi: '{store.AppId}'. Sinxronizasiya başlayır...", ConsoleColor.Cyan);

                // Tanks
                foreach (var tank in store.Tanks)
                {
                    await SyncTankAsync(tank);
                }

                // Probes
                foreach (var probe in store.Probes)
                {
                    await SyncProbeAsync(probe);
                }

                // Sensors
                foreach (var sensor in store.Sensors)
                {
                    await SyncSensorAsync(sensor);
                }

                // Oil Products
                foreach (var oil in store.OilProducts)
                {
                    await SyncOilProductAsync(oil);
                }

                // Densities
                foreach (var density in store.Densities)
                {
                    await SyncDensityAsync(density);
                }

                // Gas Sensors
                foreach (var gas in store.GasSensors)
                {
                    await SyncGasSensorAsync(gas);
                }

                // Tank Tables
                var tankNos = store.TankTable.Select(t => t.TankNo).Distinct().ToList();
                foreach (var tankNo in tankNos)
                {
                    var entries = store.TankTable.Where(t => t.TankNo == tankNo).ToList();
                    await SyncTankTableAsync(tankNo, entries);
                }

                BackgroundLogger.Log("[SYNC] Bütün ayarların cihaza sinxronizasiyası tamamlandı.", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[SYNC ⚠] Startup sinxronizasiya xətası: {ex.Message}", ConsoleColor.Red);
            }
        }

        public async Task SyncTankAsync(TankSetting tank)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<DeviceSettingsStore>();

                var requestData = new
                {
                    // İndi (Dinamik):
                    iotDevID = store.DeviceId, 
                    tankList = new[]
                    {
                        new
                        {
                            tankNo = tank.TankNo,
                            tankVer = tank.Version, // Vacib: Versiya göndərilməlidir
                            diameter = tank.DiameterMm.ToString(), // Vacib: Rəqəm yox, String olmalıdır
                            volume = tank.VolumeLiters.ToString(), // Vacib: String olmalıdır
                            used = tank.Enabled ? "1" : "0"
                        }
                    }
                };

                await _apiClient.UploadTankDataAsync(requestData);
                BackgroundLogger.Log($"[SYNC] Tank {tank.TankNo} yerli yaddaşda yeniləndi və cihaza birbaşa göndərildi.", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[SYNC ⚠] Tank {tank.TankNo} cihaza birbaşa göndərmə xətası: {ex.Message}", ConsoleColor.Red);
            }

            // Həmişə yerli verilənlər bazasını yeniləyin (HTTP uğursuz olsa belə)
            await SyncLegacyTankConfigAsync(tank);
        }

        private async Task SyncLegacyTankConfigAsync(TankSetting tank)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WindbellDbContext>();
                
                // Requirement: Use int.Parse(tankNo) to match TankOid (int)
                int tankOid = int.Parse(tank.TankNo);
                string oilName = tank.OilName ?? "";
                
                // Yanacaq kodlarına əsasən konvertasiya
                int yanacaqCode = oilName switch
                {
                    "Dizel" => 1,
                    "AI-92" => 2,
                    "Premium" => 3,
                    "M.Qaz" => 4,
                    "Super" => 5,
                    "Metan" => 6,
                    "Propan" => 7,
                    "Dizel*" => 8,
                    _ => 0
                };
                
                if (tank.Enabled)
                {
                    // MSSQL 2005 üçün UPSERT (MERGE yoxdur)
                    // Yeni çən əlavə edildikdə (INSERT), SmenN və Ip dəyərlərini mövcud ilk çəndən miras alsın.
                    string query = @"
                        DECLARE @smenN INT, @ip NVARCHAR(50);
                        
                        -- İlk mövcud çəndən dəyərləri götür (ən aşağı TankOid)
                        SELECT TOP 1 @smenN = SmenN, @ip = Ip 
                        FROM TankConfig 
                        ORDER BY TankOid ASC;
                        
                        -- Əgər cədvəl boşdursa, standart dəyərlər istifadə edilsin
                        IF @smenN IS NULL
                        BEGIN
                            SET @smenN = 1;
                            SET @ip = '192.168.0.1';
                        END

                        IF EXISTS (SELECT 1 FROM TankConfig WHERE TankOid = @p0)
                            UPDATE TankConfig 
                            SET TankFyelName = @p1, YanacaqCode = @p2, TankCapacity = @p3, capacity = @p3, TankDiametr = @p4, LastUpdate = GETDATE() 
                            WHERE TankOid = @p0
                        ELSE
                            INSERT INTO TankConfig (TankOid, TankFyelName, YanacaqCode, TankCapacity, capacity, TankDiametr, SmenN, Ip, LastUpdate) 
                            VALUES (@p0, @p1, @p2, @p3, @p3, @p4, @smenN, @ip, GETDATE())";
                    
                    await db.Database.ExecuteSqlRawAsync(
                        query, 
                        tankOid, 
                        oilName, 
                        yanacaqCode, 
                        tank.VolumeLiters,
                        tank.DiameterMm);
                        
                    BackgroundLogger.Log($"[SYNC] Köhnə TankConfig (ofisServer) cədvəli güncəlləndi (TankOid: {tankOid}, Yanacaq: {yanacaqCode}, Miras: @smenN/@ip).", ConsoleColor.DarkGreen);
                }
                else
                {
                    // Requirement: If Enabled == false, DELETE from TankConfig
                    string deleteQuery = "DELETE FROM TankConfig WHERE TankOid = @p0";
                    await db.Database.ExecuteSqlRawAsync(deleteQuery, tankOid);
                    BackgroundLogger.Log($"[SYNC] Tank {tankOid} deaktiv edildiyi üçün TankConfig cədvəlindən silindi.", ConsoleColor.DarkYellow);
                }
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[SYNC ⚠] Köhnə TankConfig yenilənmə xətası: {ex.Message}", ConsoleColor.DarkRed);
            }
        }

        public async Task SyncProbeAsync(ProbeSetting probe)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<DeviceSettingsStore>();

                var requestData = new
                {
                    // İndi (Dinamik):
                    iotDevID = store.DeviceId,
                    probeList = new[]
                    {
                        new
                        {
                            tankNo = probe.TankNo,
                            probeId = probe.ProbeId,
                            probeVer = probe.Version, // Vacib: Versiya göndərilməlidir
                            probeType = probe.IsDensityProbe ? "1" : "0",
                            oilOffset = probe.OilOffsetMm.ToString("F1", CultureInfo.InvariantCulture),
                            waterOffset = probe.WaterOffsetMm.ToString("F1", CultureInfo.InvariantCulture),
                            oilBlind = probe.OilBlindMm.ToString("F1", CultureInfo.InvariantCulture),
                            highWarning = probe.HighWarningMm.ToString("F1", CultureInfo.InvariantCulture),
                            highAlarm = probe.HighAlarmMm.ToString("F1", CultureInfo.InvariantCulture),
                            lowWarning = probe.LowWarningMm.ToString("F1", CultureInfo.InvariantCulture),
                            lowAlarm = probe.LowAlarmMm.ToString("F1", CultureInfo.InvariantCulture),
                            waterWarning = probe.WaterWarningMm.ToString("F1", CultureInfo.InvariantCulture),
                            waterAlarm = probe.WaterAlarmMm.ToString("F1", CultureInfo.InvariantCulture),
                            highTemp = probe.HighTempC.ToString("F1", CultureInfo.InvariantCulture),
                            lowTemp = probe.LowTempC.ToString("F1", CultureInfo.InvariantCulture),
                            remark = "" // Vacib: Boş olsa da göndərilməlidir
                        }
                    }
                };

                await _apiClient.UploadProbeDataAsync(requestData);
                BackgroundLogger.Log($"[SYNC] Prob {probe.ProbeId} yerli yaddaşda yeniləndi və cihaza birbaşa göndərildi.", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[SYNC ⚠] Prob {probe.ProbeId} cihaza birbaşa göndərmə xətası: {ex.Message}", ConsoleColor.Red);
            }
        }

        public async Task SyncSensorAsync(SensorSetting sensor)
        {
            BackgroundLogger.Log($"[SYNC] Sensor {sensor.SensorNo} yerli yaddaşda yeniləndi (Cihazın Heartbeat-də çəkməsi gözlənilir).", ConsoleColor.DarkGreen);
            await Task.CompletedTask;
        }

        public async Task SyncOilProductAsync(OilProductSetting oil)
        {
            BackgroundLogger.Log($"[SYNC] Yağ məhsulu {oil.OilCode} yerli yaddaşda yeniləndi (Cihazın Heartbeat-də çəkməsi gözlənilir).", ConsoleColor.DarkGreen);
            await Task.CompletedTask;
        }

        public async Task SyncDensityAsync(DensitySetting density)
        {
            BackgroundLogger.Log($"[SYNC] Sıxlıq {density.TankNo} yerli yaddaşda yeniləndi (Cihazın Heartbeat-də çəkməsi gözlənilir).", ConsoleColor.DarkGreen);
            await Task.CompletedTask;
        }

        public async Task SyncGasSensorAsync(GasSensorSetting gas)
        {
            BackgroundLogger.Log($"[SYNC] Qaz sensoru {gas.SensorNo} yerli yaddaşda yeniləndi (Cihazın Heartbeat-də çəkməsi gözlənilir).", ConsoleColor.DarkGreen);
            await Task.CompletedTask;
        }

        public async Task SyncTankTableAsync(string tankNo, System.Collections.Generic.List<TankTableEntry> entries)
        {
            BackgroundLogger.Log($"[SYNC] Tank {tankNo} cədvəli ({entries.Count} giriş) yerli yaddaşda yeniləndi (Cihazın Heartbeat-də çəkməsi gözlənilir).", ConsoleColor.DarkGreen);
            await Task.CompletedTask;
        }
    }
}
