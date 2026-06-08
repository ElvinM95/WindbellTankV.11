using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WindbellTank.Models;

namespace WindbellTank.Services
{
    /// <summary>
    /// Konsol menyusundan çağırılan bütün ayar metodları.
    /// Hər dəyişiklik DeviceSettingsStore-a yazılır və versiya artırılır,
    /// cihaz növbəti heartbeat-də yeni ayarları avtomatik çəkir.
    /// </summary>
    public class SettingsManager
    {
        private readonly DeviceSettingsStore _store;

        public SettingsManager(DeviceSettingsStore store) => _store = store;

        // ══════════════════════════════════════════════════════════════
        //  TANK AYARLARI
        // ══════════════════════════════════════════════════════════════

        public async Task SetTankAsync(string tankNo, string oilCode, string oilName,
            int diameterMm, int volumeLiters, string expansionRate, bool enabled)
        {
            await _store.UpdateTankAsync(new TankSetting
            {
                TankNo        = tankNo,
                OilCode       = oilCode,
                OilName       = oilName,
                DiameterMm    = diameterMm,
                VolumeLiters  = volumeLiters,
                ExpansionRate = expansionRate,
                Enabled       = enabled
            });
            PrintSuccess($"Tank {tankNo} ayarı yadda saxlanıldı. Cihaz ~10 san-da çəkəcək.");
        }

        public async Task ToggleTankAsync(string tankNo, bool enabled)
        {
            var existing = _store.Tanks.FirstOrDefault(t => t.TankNo == tankNo);
            if (existing == null)
            {
                PrintError($"Tank {tankNo} tapılmadı. Əvvəlcə tank əlavə edin.");
                return;
            }
            existing.Enabled = enabled;
            await _store.UpdateTankAsync(existing);
            PrintSuccess($"Tank {tankNo} {(enabled ? "AKTİV" : "DEAKTİV")} edildi.");
        }

        public void ListTanks()
        {
            if (!_store.Tanks.Any())
            {
                PrintWarning("Heç bir tank ayarlanmayıb.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  ┌────────┬──────────┬──────────────┬────────────┬────────────┬────────┐");
            Console.WriteLine("  │ Tank № │ Kod      │ Məhsul       │ Diametr mm │ Həcm Litr  │ Status │");
            Console.WriteLine("  ├────────┼──────────┼──────────────┼────────────┼────────────┼────────┤");
            foreach (var t in _store.Tanks.OrderBy(t => t.TankNo))
            {
                string status = t.Enabled ? " ✔ ON " : " ✘ OFF";
                Console.ForegroundColor = t.Enabled ? ConsoleColor.Green : ConsoleColor.DarkGray;
                Console.WriteLine($"  │   {t.TankNo,-4} │ {t.OilCode,-8} │ {t.OilName,-12} │ {t.DiameterMm,10} │ {t.VolumeLiters,10} │{status}│");
            }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  └────────┴──────────┴──────────────┴────────────┴────────────┴────────┘");
            Console.ResetColor();
        }

        // ══════════════════════════════════════════════════════════════
        //  PROB KONFİQURASİYASI
        // ══════════════════════════════════════════════════════════════

        public async Task SetProbeAsync(string tankNo, string probeId, bool isDensityProbe,
            double oilOffset, double waterOffset, double oilBlind,
            double highWarning, double highAlarm,
            double lowWarning, double lowAlarm,
            double waterWarning, double waterAlarm,
            double highTemp, double lowTemp)
        {
            await _store.UpdateProbeAsync(new ProbeSetting
            {
                TankNo         = tankNo,
                ProbeId        = probeId,
                IsDensityProbe = isDensityProbe,
                OilOffsetMm    = oilOffset,
                WaterOffsetMm  = waterOffset,
                OilBlindMm     = oilBlind,
                HighWarningMm  = highWarning,
                HighAlarmMm    = highAlarm,
                LowWarningMm   = lowWarning,
                LowAlarmMm     = lowAlarm,
                WaterWarningMm  = waterWarning,
                WaterAlarmMm    = waterAlarm,
                HighTempC       = highTemp,
                LowTempC        = lowTemp
            });
            PrintSuccess($"Prob (Tank {tankNo}, ID: {probeId}) ayarı yadda saxlanıldı.");
        }

        public void ListProbes()
        {
            if (!_store.Probes.Any())
            {
                PrintWarning("Heç bir prob ayarlanmayıb.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("\n  ┌────────┬──────────┬────────┬────────────┬────────────┬────────────┬────────────┐");
            Console.WriteLine("  │ Tank № │ Prob ID  │ Tip    │ HighAlarm  │ LowAlarm   │ WaterAlarm │ Temp (H/L) │");
            Console.WriteLine("  ├────────┼──────────┼────────┼────────────┼────────────┼────────────┼────────────┤");
            foreach (var p in _store.Probes.OrderBy(p => p.TankNo))
            {
                string pType = p.IsDensityProbe ? "Sıxlıq" : "Normal";
                Console.WriteLine($"  │   {p.TankNo,-4} │ {p.ProbeId,-8} │ {pType,-6} │ {p.HighAlarmMm,10:F1} │ {p.LowAlarmMm,10:F1} │ {p.WaterAlarmMm,10:F1} │ {p.HighTempC:F0}/{p.LowTempC:F0}    │");
            }
            Console.WriteLine("  └────────┴──────────┴────────┴────────────┴────────────┴────────────┴────────────┘");
            Console.ResetColor();
        }

        // ══════════════════════════════════════════════════════════════
        //  TANK CƏDVƏLİ (Hündürlük-Həcm Kalibrasyası)
        // ══════════════════════════════════════════════════════════════

        public async Task SetTankTableAsync(string tankNo, List<(int height, int volume)> entries)
        {
            var tableEntries = entries.Select(e => new TankTableEntry
            {
                TankNo       = tankNo,
                HeightMm     = e.height,
                VolumeLiters = e.volume
            }).ToList();

            await _store.UpdateTankTableAsync(tankNo, tableEntries);
            PrintSuccess($"Tank {tankNo} cədvəlinə {entries.Count} giriş yazıldı.");
        }

        public void ListTankTable(string tankNo)
        {
            var entries = _store.TankTable.Where(e => e.TankNo == tankNo).OrderBy(e => e.HeightMm).ToList();
            if (!entries.Any())
            {
                PrintWarning($"Tank {tankNo} üçün cədvəl tapılmadı.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  Tank {tankNo} Cədvəli ({entries.Count} giriş):");
            Console.WriteLine("  ┌──────────────┬──────────────┐");
            Console.WriteLine("  │ Hündürlük mm │ Həcm Litr    │");
            Console.WriteLine("  ├──────────────┼──────────────┤");
            foreach (var e in entries)
                Console.WriteLine($"  │ {e.HeightMm,12} │ {e.VolumeLiters,12} │");
            Console.WriteLine("  └──────────────┴──────────────┘");
            Console.ResetColor();
        }

        // ══════════════════════════════════════════════════════════════
        //  SIZINTI SENSORU
        // ══════════════════════════════════════════════════════════════

        public async Task SetSensorAsync(string sensorNo, string sensorType, string position,
            string positionNum, bool enabled)
        {
            await _store.UpdateSensorAsync(new SensorSetting
            {
                SensorNo    = sensorNo,
                SensorType  = sensorType,
                Position    = position,
                PositionNum = positionNum,
                Enabled     = enabled
            });
            PrintSuccess($"Sensor {sensorNo} ayarı yadda saxlanıldı.");
        }

        public void ListSensors()
        {
            if (!_store.Sensors.Any())
            {
                PrintWarning("Heç bir sızıntı sensoru ayarlanmayıb.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("\n  ┌───────────┬──────┬─────────┬───────────┬────────┐");
            Console.WriteLine("  │ Sensor №  │ Tip  │ Mövqe   │ Mövqe №   │ Status │");
            Console.WriteLine("  ├───────────┼──────┼─────────┼───────────┼────────┤");
            foreach (var s in _store.Sensors.OrderBy(s => s.SensorNo))
            {
                string pos = s.Position switch
                {
                    "0" => "Ada    ",
                    "1" => "Boşalt.",
                    "2" => "Quyu   ",
                    "3" => "Digər  ",
                    _ => s.Position.PadRight(7)
                };
                string status = s.Enabled ? " ✔ ON " : " ✘ OFF";
                Console.WriteLine($"  │    {s.SensorNo,-6} │  {s.SensorType,-3} │ {pos} │    {s.PositionNum,-6} │{status}│");
            }
            Console.WriteLine("  └───────────┴──────┴─────────┴───────────┴────────┘");
            Console.ResetColor();
        }

        // ══════════════════════════════════════════════════════════════
        //  YAĞ MƏHSULU AYARLARI
        // ══════════════════════════════════════════════════════════════

        public async Task SetOilProductAsync(string oilCode, string oilName, string oilColor,
            string expansionRate, string temperature, string weightDensity)
        {
            await _store.UpdateOilProductAsync(new OilProductSetting
            {
                OilCode       = oilCode,
                OilName       = oilName,
                OilColor      = oilColor,
                ExpansionRate = expansionRate,
                Temperature   = temperature,
                WeightDensity = weightDensity
            });
            PrintSuccess($"Yağ məhsulu '{oilName}' (Kod: {oilCode}) yadda saxlanıldı.");
        }

        public void ListOilProducts()
        {
            if (!_store.OilProducts.Any())
            {
                PrintWarning("Heç bir yağ məhsulu ayarlanmayıb.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n  ┌──────────┬──────────────┬────────┬──────────┬──────┬─────────┐");
            Console.WriteLine("  │ Kod      │ Məhsul       │ Rəng   │ Genişlən │ Temp │ Sıxlıq  │");
            Console.WriteLine("  ├──────────┼──────────────┼────────┼──────────┼──────┼─────────┤");
            foreach (var o in _store.OilProducts)
                Console.WriteLine($"  │ {o.OilCode,-8} │ {o.OilName,-12} │ {o.OilColor,-6} │ {o.ExpansionRate,-8} │ {o.Temperature,-4} │ {o.WeightDensity,-7} │");
            Console.WriteLine("  └──────────┴──────────────┴────────┴──────────┴──────┴─────────┘");
            Console.ResetColor();
        }

        // ══════════════════════════════════════════════════════════════
        //  SIXLIQ AYARLARI
        // ══════════════════════════════════════════════════════════════

        public async Task SetDensityAsync(string tankNo, string heightDiff, string fixRate,
            string initDensity, string secondDensity, string densityFloatNo)
        {
            await _store.UpdateDensityAsync(new DensitySetting
            {
                TankNo         = tankNo,
                HeightDiff     = heightDiff,
                FixRate        = fixRate,
                InitDensity    = initDensity,
                SecondDensity  = secondDensity,
                DensityFloatNo = densityFloatNo
            });
            PrintSuccess($"Tank {tankNo} sıxlıq ayarı yadda saxlanıldı.");
        }

        public void ListDensities()
        {
            if (!_store.Densities.Any())
            {
                PrintWarning("Heç bir sıxlıq ayarı yoxdur.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("\n  ┌────────┬──────────┬──────────┬──────────┬──────────┬──────────┐");
            Console.WriteLine("  │ Tank № │ Hünd.Fq  │ FixRate  │ İlkin    │ İkinci   │ Float №  │");
            Console.WriteLine("  ├────────┼──────────┼──────────┼──────────┼──────────┼──────────┤");
            foreach (var d in _store.Densities.OrderBy(d => d.TankNo))
                Console.WriteLine($"  │   {d.TankNo,-4} │ {d.HeightDiff,-8} │ {d.FixRate,-8} │ {d.InitDensity,-8} │ {d.SecondDensity,-8} │ {d.DensityFloatNo,-8} │");
            Console.WriteLine("  └────────┴──────────┴──────────┴──────────┴──────────┴──────────┘");
            Console.ResetColor();
        }

        // ══════════════════════════════════════════════════════════════
        //  QAZ SENSORU AYARLARI
        // ══════════════════════════════════════════════════════════════

        public async Task SetGasSensorAsync(string sensorNo, string position, string positionNum, bool enabled)
        {
            await _store.UpdateGasSensorAsync(new GasSensorSetting
            {
                SensorNo    = sensorNo,
                Position    = position,
                PositionNum = positionNum,
                Enabled     = enabled
            });
            PrintSuccess($"Qaz sensoru {sensorNo} yadda saxlanıldı.");
        }

        public void ListGasSensors()
        {
            if (!_store.GasSensors.Any())
            {
                PrintWarning("Heç bir qaz sensoru ayarlanmayıb.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine("\n  ┌───────────┬─────────┬───────────┬────────┐");
            Console.WriteLine("  │ Sensor №  │ Mövqe   │ Mövqe №   │ Status │");
            Console.WriteLine("  ├───────────┼─────────┼───────────┼────────┤");
            foreach (var g in _store.GasSensors.OrderBy(g => g.SensorNo))
            {
                string pos = g.Position switch
                {
                    "0" => "Ada    ",
                    "1" => "Boşalt.",
                    "2" => "Quyu   ",
                    "3" => "Digər  ",
                    _ => g.Position.PadRight(7)
                };
                string status = g.Enabled ? " ✔ ON " : " ✘ OFF";
                Console.WriteLine($"  │    {g.SensorNo,-6} │ {pos} │    {g.PositionNum,-6} │{status}│");
            }
            Console.WriteLine("  └───────────┴─────────┴───────────┴────────┘");
            Console.ResetColor();
        }

        // ══════════════════════════════════════════════════════════════
        //  BÜTÜN AYARLARIN ÜMUMİ GÖSTƏRİŞİ
        // ══════════════════════════════════════════════════════════════

        public void PrintAllSettings()
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\n══════════════════════════════════════════════════════════");
            Console.WriteLine("            BÜTÜN CİHAZ AYARLARININ İCMALI");
            Console.WriteLine("══════════════════════════════════════════════════════════");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Versiyalar: Tank={_store.TankVer} | Prob={_store.ProbeVer} | Sensor={_store.SensorVer}");
            Console.WriteLine($"              Cədvəl={_store.TableVer} | Yağ={_store.OilProductVer} | Sıxlıq={_store.DensityVer} | Qaz={_store.GasVer}");
            Console.ResetColor();

            ListTanks();
            ListProbes();
            ListOilProducts();
            ListDensities();
            ListSensors();
            ListGasSensors();

            // Tank cədvəlləri
            var tankNos = _store.TankTable.Select(e => e.TankNo).Distinct().OrderBy(n => n);
            foreach (var tno in tankNos)
                ListTankTable(tno);
        }

        // ══════════════════════════════════════════════════════════════
        //  NÜMUNƏ AYARLARIN YÜKLƏNMƏSİ
        // ══════════════════════════════════════════════════════════════

        public async Task AddSampleSettingsAsync()
        {
            // Tank 1
            await SetTankAsync("01", "1020", "92#", 2000, 20000, "0.0012", true);

            // Prob 1
            await SetProbeAsync("01", "100001", false,
                0.0, 0.0, 50.0,
                1800.0, 1900.0,
                200.0, 100.0,
                30.0, 50.0,
                55.0, -40.0);

            // Yağ məhsulu
            await SetOilProductAsync("1020", "92#", "green", "0.0012", "20", "0.725");

            // Sıxlıq
            await SetDensityAsync("01", "0", "1.0", "0.725", "0.720", "1");

            // Sızıntı sensoru
            await SetSensorAsync("01", "0", "0", "01", true);

            // Qaz sensoru
            await SetGasSensorAsync("01", "0", "01", true);

            // Tank cədvəli (nümunə)
            await SetTankTableAsync("01", new List<(int, int)>
            {
                (0, 0), (100, 500), (200, 1200), (300, 2100),
                (400, 3200), (500, 4500), (600, 6000), (700, 7700),
                (800, 9600), (900, 11700), (1000, 14000),
                (1100, 16000), (1200, 17800), (1300, 19200), (1400, 20000)
            });

            PrintSuccess("Nümunə ayarları uğurla yükləndi.");
        }

        // ── Yardımçı Çap Metodları ────────────────────────────────
        private static void PrintSuccess(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✔ {msg}");
            Console.ResetColor();
        }

        private static void PrintWarning(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  ⚠ {msg}");
            Console.ResetColor();
        }

        private static void PrintError(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ✘ {msg}");
            Console.ResetColor();
        }
    }
}
