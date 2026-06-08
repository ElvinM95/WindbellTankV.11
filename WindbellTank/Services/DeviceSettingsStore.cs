using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WindbellTank.Models;

namespace WindbellTank.Services
{
    /// <summary>
    /// Cihaz ayarlarını həm yaddaşda (sürətli oxumaq üçün) həm də SQL bazasında saxlayır.
    /// Write-Through: Hər yeniləmə əvvəlcə SQL-ə yazılır, sonra yaddaşda yenilənir.
    /// </summary>
    public class DeviceSettingsStore
    {
        private readonly IServiceScopeFactory? _scopeFactory;

        // Cihazın iotDevID-si (heartbeat-dən dinamik öyrənilir)
        public string AppId { get; set; } = "2343554";
        public string DeviceId { get; set; } = "unknown"; // Bu isə cihazın öz nömrəsidir (Dinamik olacaq)

        // İlk heartbeat gəlməsini gözləmək üçün siqnal
        private readonly TaskCompletionSource<bool> _appIdReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Heartbeat-dən gələn iotDevID ilə AppId-ni yeniləyir və gözləyən tapşırıqları azad edir.
        /// </summary>
        public void SetAppId(string iotDevId)
        {
            AppId = iotDevId;
            _appIdReady.TrySetResult(true);
        }

        /// <summary>
        /// İlk heartbeat gələnə qədər gözləyir (maks. 30 san).
        /// </summary>
        public async Task WaitForAppIdAsync(CancellationToken ct = default)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                await _appIdReady.Task.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                BackgroundLogger.Log($"[SYNC ⚠] Heartbeat gözləmə vaxtı bitdi, default AppId '{AppId}' istifadə olunacaq.", ConsoleColor.Yellow);
            }
        }

        // Versiyalar — artırıldıqda cihaz yeni ayarları çəkir
        public int TankVer    { get; private set; } = 1;
        public int ProbeVer   { get; private set; } = 1;
        public int SensorVer  { get; private set; } = 1;
        public int TableVer   { get; private set; } = 1;
        public int DensityVer { get; private set; } = 1;
        public int OilProductVer { get; private set; } = 1;
        public int GasVer { get; private set; } = 1;

        // Ayarlar (yaddaş keşi — sürətli oxumaq üçün)
        public List<TankSetting>   Tanks   { get; } = new();
        public List<ProbeSetting>  Probes  { get; } = new();
        public List<SensorSetting> Sensors { get; } = new();
        public List<TankTableEntry> TankTable { get; } = new();
        public List<OilProductSetting> OilProducts { get; } = new();
        public List<DensitySetting> Densities { get; } = new();
        public List<GasSensorSetting> GasSensors { get; } = new();

        public DeviceSettingsStore() { }

        public DeviceSettingsStore(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Proqram başladıqda SQL bazasından bütün ayarları yaddaşa yükləyir.
        /// </summary>
        public void LoadFromDatabase()
        {
            if (_scopeFactory == null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WindbellDbContext>();

                // Versiyaları yüklə
                var sysVer = db.SystemVersions.FirstOrDefault(v => v.Id == 1);
                if (sysVer != null)
                {
                    TankVer       = sysVer.TankVer;
                    ProbeVer      = sysVer.ProbeVer;
                    SensorVer     = sysVer.SensorVer;
                    TableVer      = sysVer.TableVer;
                    DensityVer    = sysVer.DensityVer;
                    OilProductVer = sysVer.OilProductVer;
                    GasVer        = sysVer.GasVer;
                }

                // Ayarları yüklə
                Tanks.Clear();
                Tanks.AddRange(db.TankSettings.ToList());

                Probes.Clear();
                Probes.AddRange(db.ProbeSettings.ToList());

                Sensors.Clear();
                Sensors.AddRange(db.SensorSettings.ToList());

                TankTable.Clear();
                TankTable.AddRange(db.TankTableEntries.ToList());

                OilProducts.Clear();
                OilProducts.AddRange(db.OilProductSettings.ToList());

                Densities.Clear();
                Densities.AddRange(db.DensitySettings.ToList());

                GasSensors.Clear();
                GasSensors.AddRange(db.GasSensorSettings.ToList());

                Console.WriteLine($"[DB ✔] Bazadan yükləndi: {Tanks.Count} tank, {Probes.Count} prob, {Sensors.Count} sensor, " +
                    $"{OilProducts.Count} yağ, {Densities.Count} sıxlıq, {GasSensors.Count} qaz sensoru, {TankTable.Count} cədvəl girişi");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB ⚠] Bazadan yükləmə xətası: {ex.Message}");
            }
        }

        // ── Versiya yeniləmə (SQL-ə yazma) ──────────────────────
        private async Task SaveVersionsAsync(WindbellDbContext db)
        {
            var sysVer = db.SystemVersions.FirstOrDefault(v => v.Id == 1);
            if (sysVer == null)
            {
                sysVer = new SystemVersion { Id = 1 };
                db.SystemVersions.Add(sysVer);
            }
            sysVer.TankVer       = TankVer;
            sysVer.ProbeVer      = ProbeVer;
            sysVer.SensorVer     = SensorVer;
            sysVer.TableVer      = TableVer;
            sysVer.DensityVer    = DensityVer;
            sysVer.OilProductVer = OilProductVer;
            sysVer.GasVer        = GasVer;
            await db.SaveChangesAsync();
        }

        // ── Tank ayarını yenilə ──────────────────────────────────
        public async Task UpdateTankAsync(TankSetting setting)
        {
            // Yaddaşda yenilə
            var existing = Tanks.FirstOrDefault(t => t.TankNo == setting.TankNo);
            if (existing != null) Tanks.Remove(existing);
            setting.Version = (TankVer + 1).ToString();
            Tanks.Add(setting);
            TankVer++;

            // SQL-ə yaz
            if (_scopeFactory != null)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<WindbellDbContext>();

                    var dbEntity = db.TankSettings.FirstOrDefault(t => t.TankNo == setting.TankNo);
                    if (dbEntity != null)
                    {
                        dbEntity.OilCode       = setting.OilCode;
                        dbEntity.OilName       = setting.OilName;
                        dbEntity.DiameterMm    = setting.DiameterMm;
                        dbEntity.VolumeLiters  = setting.VolumeLiters;
                        dbEntity.ExpansionRate = setting.ExpansionRate;
                        dbEntity.Enabled       = setting.Enabled;
                        dbEntity.Version       = setting.Version;
                    }
                    else
                    {
                        db.TankSettings.Add(setting);
                    }
                    await SaveVersionsAsync(db);

                    var syncService = scope.ServiceProvider.GetRequiredService<DeviceSyncService>();
                    await syncService.SyncTankAsync(setting);
                }
                catch (Exception ex)
                {
                    BackgroundLogger.Log($"[DB ⚠] Tank SQL yazma xətası: {ex.Message}", ConsoleColor.DarkRed);
                }
            }

            Console.WriteLine($"[STORE] Tank {setting.TankNo} yeniləndi. Yeni ver: {TankVer}");
        }

        // ── Probe ayarını yenilə ─────────────────────────────────
        public async Task UpdateProbeAsync(ProbeSetting setting)
        {
            var existing = Probes.FirstOrDefault(p => p.TankNo == setting.TankNo);
            if (existing != null) Probes.Remove(existing);
            setting.Version = (ProbeVer + 1).ToString();
            Probes.Add(setting);
            ProbeVer++;

            if (_scopeFactory != null)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<WindbellDbContext>();

                    var dbEntity = db.ProbeSettings.FirstOrDefault(p => p.TankNo == setting.TankNo && p.ProbeId == setting.ProbeId);
                    if (dbEntity != null)
                    {
                        dbEntity.IsDensityProbe = setting.IsDensityProbe;
                        dbEntity.OilOffsetMm    = setting.OilOffsetMm;
                        dbEntity.WaterOffsetMm  = setting.WaterOffsetMm;
                        dbEntity.OilBlindMm     = setting.OilBlindMm;
                        dbEntity.HighWarningMm  = setting.HighWarningMm;
                        dbEntity.HighAlarmMm    = setting.HighAlarmMm;
                        dbEntity.LowWarningMm   = setting.LowWarningMm;
                        dbEntity.LowAlarmMm     = setting.LowAlarmMm;
                        dbEntity.WaterWarningMm = setting.WaterWarningMm;
                        dbEntity.WaterAlarmMm   = setting.WaterAlarmMm;
                        dbEntity.HighTempC      = setting.HighTempC;
                        dbEntity.LowTempC       = setting.LowTempC;
                        dbEntity.Version        = setting.Version;
                    }
                    else
                    {
                        db.ProbeSettings.Add(setting);
                    }
                    await SaveVersionsAsync(db);

                    var syncService = scope.ServiceProvider.GetRequiredService<DeviceSyncService>();
                    await syncService.SyncProbeAsync(setting);
                }
                catch (Exception ex)
                {
                    BackgroundLogger.Log($"[DB ⚠] Prob SQL yazma xətası: {ex.Message}", ConsoleColor.DarkRed);
                }
            }
        }

        // ── Sensor ayarını yenilə ────────────────────────────────
        public async Task UpdateSensorAsync(SensorSetting setting)
        {
            var existing = Sensors.FirstOrDefault(s => s.SensorNo == setting.SensorNo);
            if (existing != null) Sensors.Remove(existing);
            Sensors.Add(setting);
            SensorVer++;

            if (_scopeFactory != null)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<WindbellDbContext>();

                    var dbEntity = db.SensorSettings.FirstOrDefault(s => s.SensorNo == setting.SensorNo);
                    if (dbEntity != null)
                    {
                        dbEntity.SensorType  = setting.SensorType;
                        dbEntity.Position    = setting.Position;
                        dbEntity.PositionNum = setting.PositionNum;
                        dbEntity.Enabled     = setting.Enabled;
                    }
                    else
                    {
                        db.SensorSettings.Add(setting);
                    }
                    await SaveVersionsAsync(db);

                    var syncService = scope.ServiceProvider.GetRequiredService<DeviceSyncService>();
                    await syncService.SyncSensorAsync(setting);
                }
                catch (Exception ex)
                {
                    BackgroundLogger.Log($"[DB ⚠] Sensor SQL yazma xətası: {ex.Message}", ConsoleColor.DarkRed);
                }
            }
        }

        // ── Tank cədvəlini yenilə ────────────────────────────────
        public async Task UpdateTankTableAsync(string tankNo, List<TankTableEntry> entries)
        {
            TankTable.RemoveAll(t => t.TankNo == tankNo);
            TankTable.AddRange(entries);
            TableVer++;

            if (_scopeFactory != null)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<WindbellDbContext>();

                    var existing = db.TankTableEntries.Where(t => t.TankNo == tankNo).ToList();
                    db.TankTableEntries.RemoveRange(existing);
                    db.TankTableEntries.AddRange(entries);
                    await SaveVersionsAsync(db);

                    var syncService = scope.ServiceProvider.GetRequiredService<DeviceSyncService>();
                    await syncService.SyncTankTableAsync(tankNo, entries);
                }
                catch (Exception ex)
                {
                    BackgroundLogger.Log($"[DB ⚠] Tank cədvəli SQL yazma xətası: {ex.Message}", ConsoleColor.DarkRed);
                }
            }
        }

        // ── Yağ məhsulu ayarını yenilə ──────────────────────────
        public async Task UpdateOilProductAsync(OilProductSetting setting)
        {
            var existing = OilProducts.FirstOrDefault(o => o.OilCode == setting.OilCode);
            if (existing != null) OilProducts.Remove(existing);
            OilProducts.Add(setting);
            OilProductVer++;

            if (_scopeFactory != null)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<WindbellDbContext>();

                    var dbEntity = db.OilProductSettings.FirstOrDefault(o => o.OilCode == setting.OilCode);
                    if (dbEntity != null)
                    {
                        dbEntity.OilName       = setting.OilName;
                        dbEntity.OilColor      = setting.OilColor;
                        dbEntity.ExpansionRate = setting.ExpansionRate;
                        dbEntity.Temperature   = setting.Temperature;
                        dbEntity.WeightDensity = setting.WeightDensity;
                    }
                    else
                    {
                        db.OilProductSettings.Add(setting);
                    }
                    await SaveVersionsAsync(db);

                    var syncService = scope.ServiceProvider.GetRequiredService<DeviceSyncService>();
                    await syncService.SyncOilProductAsync(setting);
                }
                catch (Exception ex)
                {
                    BackgroundLogger.Log($"[DB ⚠] Yağ məhsulu SQL yazma xətası: {ex.Message}", ConsoleColor.DarkRed);
                }
            }

            Console.WriteLine($"[STORE] Yağ məhsulu '{setting.OilName}' yeniləndi. Ver: {OilProductVer}");
        }

        // ── Sıxlıq ayarını yenilə ───────────────────────────────
        public async Task UpdateDensityAsync(DensitySetting setting)
        {
            var existing = Densities.FirstOrDefault(d => d.TankNo == setting.TankNo);
            if (existing != null) Densities.Remove(existing);
            setting.Version = (DensityVer + 1).ToString();
            Densities.Add(setting);
            DensityVer++;

            if (_scopeFactory != null)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<WindbellDbContext>();

                    var dbEntity = db.DensitySettings.FirstOrDefault(d => d.TankNo == setting.TankNo);
                    if (dbEntity != null)
                    {
                        dbEntity.HeightDiff     = setting.HeightDiff;
                        dbEntity.FixRate        = setting.FixRate;
                        dbEntity.InitDensity    = setting.InitDensity;
                        dbEntity.SecondDensity  = setting.SecondDensity;
                        dbEntity.DensityFloatNo = setting.DensityFloatNo;
                        dbEntity.Remark         = setting.Remark;
                        dbEntity.Version        = setting.Version;
                    }
                    else
                    {
                        db.DensitySettings.Add(setting);
                    }
                    await SaveVersionsAsync(db);

                    var syncService = scope.ServiceProvider.GetRequiredService<DeviceSyncService>();
                    await syncService.SyncDensityAsync(setting);
                }
                catch (Exception ex)
                {
                    BackgroundLogger.Log($"[DB ⚠] Sıxlıq SQL yazma xətası: {ex.Message}", ConsoleColor.DarkRed);
                }
            }

            Console.WriteLine($"[STORE] Tank {setting.TankNo} sıxlıq ayarı yeniləndi. Ver: {DensityVer}");
        }

        // ── Qaz sensoru ayarını yenilə ───────────────────────────
        public async Task UpdateGasSensorAsync(GasSensorSetting setting)
        {
            var existing = GasSensors.FirstOrDefault(g => g.SensorNo == setting.SensorNo);
            if (existing != null) GasSensors.Remove(existing);
            GasSensors.Add(setting);
            GasVer++;

            if (_scopeFactory != null)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<WindbellDbContext>();

                    var dbEntity = db.GasSensorSettings.FirstOrDefault(g => g.SensorNo == setting.SensorNo);
                    if (dbEntity != null)
                    {
                        dbEntity.Position    = setting.Position;
                        dbEntity.PositionNum = setting.PositionNum;
                        dbEntity.Enabled     = setting.Enabled;
                    }
                    else
                    {
                        db.GasSensorSettings.Add(setting);
                    }
                    await SaveVersionsAsync(db);

                    var syncService = scope.ServiceProvider.GetRequiredService<DeviceSyncService>();
                    await syncService.SyncGasSensorAsync(setting);
                }
                catch (Exception ex)
                {
                    BackgroundLogger.Log($"[DB ⚠] Qaz sensoru SQL yazma xətası: {ex.Message}", ConsoleColor.DarkRed);
                }
            }

            Console.WriteLine($"[STORE] Qaz sensoru {setting.SensorNo} yeniləndi. Ver: {GasVer}");
        }
    }
}
