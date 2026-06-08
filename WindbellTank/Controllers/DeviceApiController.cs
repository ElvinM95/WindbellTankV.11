using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using WindbellTank.Services;

namespace WindbellTank.Controllers
{
    [ApiController]
    [Route("deviceAPI")]
    public class DeviceApiController : ControllerBase
    {
        private readonly DeviceSettingsStore _store;
        // Cihazdan gelen son appId-ni yadda saxlayiriq (heartbeat body-de token olmur)
        private static string _lastKnownAppId = "2343554";
        private static string _lastKnownDeviceIp = "";

        public DeviceApiController(DeviceSettingsStore store)
            => _store = store;

        // ── HEARTBEAT — Cihaz hər 10 saniyədə çağırır ─────────────────
        // Cihaz versiyaları müqayisə edir, fərqli olanları çəkir
        [HttpPost("getSysVersionData")]
        public IActionResult GetSysVersionData([FromBody] JsonElement body)
        {
            string iotDevId = GetIotDevId(body);
            string deviceIp = "";
            var remoteIp = HttpContext.Connection.RemoteIpAddress;
            if (remoteIp != null)
            {
                if (remoteIp.IsIPv4MappedToIPv6)
                    deviceIp = remoteIp.MapToIPv4().ToString();
                else if (remoteIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    deviceIp = $"[{remoteIp}]";
                else
                    deviceIp = remoteIp.ToString();
            }
            
            if (!string.IsNullOrEmpty(deviceIp))
                _lastKnownDeviceIp = deviceIp;
            
            BackgroundLogger.Log($"[HEARTBEAT] Cihaz: {iotDevId} (IP: {deviceIp})", ConsoleColor.DarkGray);

            // Cihaz versiyalarını müqayisə et və loqla
            // Windbell protokolu push əsaslıdır: versiya fərqi olanda cihaz özü upload* endpointlərini çağırır
            CompareVersionsAndLog(body);

            var versionList = BuildTankVersionList();

            // Debug: Heartbeat cavabında göndərilən versiyaları loqla
            foreach (var t in _store.Tanks)
            {
                BackgroundLogger.Log($"[HEARTBEAT_VER] Tank {t.TankNo}: ver={t.Version}, enabled={t.Enabled}, serverTankVer={_store.TankVer}", ConsoleColor.DarkGray);
            }

            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 6,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                data        = new
                {
                    serverTime         = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    softVer            = 1,
                    sensorVer          = _store.SensorVer,
                    gasVer             = _store.GasVer,
                    deviceSettingType  = 0, // 0=Remote — cihaz uzaqdan idarə olunur
                    tankList           = versionList
                },
                msg = (string?)null
            });
        }

        private object BuildTankVersionList()
        {
            // Heç tank yoxdursa default göndər
            if (!_store.Tanks.Any())
            {
                return new[] { new
                {
                    tankNo       = "01",
                    tankVer      = _store.TankVer,
                    tankTableVer = _store.TableVer,
                    probeVer     = _store.ProbeVer,
                    densityVer   = _store.DensityVer
                }};
            }

            return _store.Tanks.Select(t => new
            {
                tankNo       = t.TankNo,
                tankVer      = int.Parse(t.Version),
                tankTableVer = _store.TableVer,
                probeVer     = _store.ProbeVer,
                densityVer   = _store.DensityVer
            });
        }

        // ── TANK AYARLARI — Cihaz versiya fərqində çağırır ────────────
        [HttpPost("getTankData")]
        public IActionResult GetTankData([FromBody] JsonElement body)
        {
            string requestedTankNo = "";
            try 
            {
                if (body.TryGetProperty("data", out JsonElement dataEl) && dataEl.TryGetProperty("tankNo", out JsonElement tankNoEl))
                {
                    requestedTankNo = tankNoEl.GetString() ?? "";
                }
            } 
            catch { }

            BackgroundLogger.Log($"[REQUEST] Cihaz tank ayarlarını tələb etdi (tankNo: '{requestedTankNo}')", ConsoleColor.DarkCyan);

            var tanksToReturn = _store.Tanks.AsEnumerable();
            if (!string.IsNullOrEmpty(requestedTankNo))
            {
                tanksToReturn = tanksToReturn.Where(t => t.TankNo == requestedTankNo.PadLeft(2, '0'));
            }

            var tankList = tanksToReturn.Select(t => new
            {
                tankNo        = t.TankNo,
                oilCode       = t.OilCode,
                oilName       = t.OilName,
                diameter      = t.DiameterMm.ToString(System.Globalization.CultureInfo.InvariantCulture),
                volume        = t.VolumeLiters.ToString(System.Globalization.CultureInfo.InvariantCulture),
                oilRate       = t.ExpansionRate,   // "0.0012" benzin
                used          = t.Enabled ? "1" : "0"
            }).ToList();

            foreach (var tank in tankList)
            {
                BackgroundLogger.Log($"[RESPONSE] → Tank {tank.tankNo}: used={tank.used}, oil={tank.oilName}, vol={tank.volume}L", ConsoleColor.DarkCyan);
            }

            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 9,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                data        = new { tankList },
                msg = (string?)null
            });
        }

        [HttpPost("uploadTankData")]
        public async Task<IActionResult> UploadTankData([FromBody] JsonElement body)
        {
            ValidateToken(body);
            try
            {
                if (body.TryGetProperty("data", out JsonElement dataEl) && dataEl.TryGetProperty("tankList", out JsonElement tankListEl))
                {
                    foreach (var t in tankListEl.EnumerateArray())
                    {
                        string tNo = GetStr(t, "tankNo").PadLeft(2, '0'); // "2" -> "02" formatına sal
                        string usedStatus = GetStr(t, "used");

                        // Server-in Enabled statusunu qoru — cihaz onu dəyişə bilməz.
                        // Yalnız konsol menyusu (SettingsManager) tankı aktiv/deaktiv edə bilər.
                        var existingTank = _store.Tanks.FirstOrDefault(x => x.TankNo == tNo);
                        bool serverEnabled = existingTank?.Enabled ?? (usedStatus == "1");

                        if (existingTank != null && existingTank.Enabled != (usedStatus == "1"))
                        {
                            BackgroundLogger.Log($"[UPLOAD ⚠] Cihaz Tank {tNo} üçün used={usedStatus} göndərdi, amma server Enabled={existingTank.Enabled}. Server statusu qorunur.", ConsoleColor.Yellow);
                        }

                        await _store.UpdateTankAsync(new WindbellTank.Models.TankSetting
                        {
                            TankNo = tNo,
                            OilCode = GetStr(t, "oilCode"),
                            OilName = GetStr(t, "oilName"),
                            DiameterMm = int.TryParse(GetStr(t, "diameter"), out int d) ? d : 0,
                            VolumeLiters = int.TryParse(GetStr(t, "volume"), out int v) ? v : 0,
                            ExpansionRate = GetStr(t, "oilRate"),
                            Enabled = serverEnabled
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[ERROR] Tank datası parse edilərkən xəta: {ex.Message}", ConsoleColor.Red);
            }
            BackgroundLogger.Log("[UPLOAD] Cihaz tank ayarlarını yüklədi və yadda saxlanıldı", ConsoleColor.DarkGreen);
            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 10,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                msg         = (string?)null
            });
        }

        // ── PROB AYARLARI ──────────────────────────────────────────────
        [HttpPost("getProbeData")]
        public IActionResult GetProbeData([FromBody] JsonElement body)
        {
            string requestedTankNo = "";
            try 
            {
                if (body.TryGetProperty("data", out JsonElement dataEl) && dataEl.TryGetProperty("tankNo", out JsonElement tankNoEl))
                {
                    requestedTankNo = tankNoEl.GetString() ?? "";
                }
            } 
            catch { }

            BackgroundLogger.Log($"[REQUEST] Cihaz prob ayarlarını tələb etdi (tankNo: '{requestedTankNo}')", ConsoleColor.DarkCyan);

            var probesToReturn = _store.Probes.AsEnumerable();
            if (!string.IsNullOrEmpty(requestedTankNo))
            {
                probesToReturn = probesToReturn.Where(p => p.TankNo == requestedTankNo.PadLeft(2, '0'));
            }

            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 11,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                data        = new
                {
                    probeList = probesToReturn.Select(p => new
                    {
                        tankNo       = p.TankNo,
                        probeId      = p.ProbeId,
                        probeType    = p.IsDensityProbe ? "1" : "0",
                        oilOffset    = p.OilOffsetMm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                        waterOffset  = p.WaterOffsetMm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                        oilBlind     = p.OilBlindMm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                        highWarning  = p.HighWarningMm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                        highAlarm    = p.HighAlarmMm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                        lowWarning   = p.LowWarningMm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                        lowAlarm     = p.LowAlarmMm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                        waterWarning = p.WaterWarningMm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                        waterAlarm   = p.WaterAlarmMm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                        highTemp     = p.HighTempC.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                        lowTemp      = p.LowTempC.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                    }).ToList()
                },
                msg = (string?)null
            });
        }

        [HttpPost("uploadProbeData")]
        public async Task<IActionResult> UploadProbeData([FromBody] JsonElement body)
        {
            ValidateToken(body);
            try
            {
                if (body.TryGetProperty("data", out JsonElement dataEl) && dataEl.TryGetProperty("probeList", out JsonElement probeListEl))
                {
                    foreach (var p in probeListEl.EnumerateArray())
                    {
                        await _store.UpdateProbeAsync(new WindbellTank.Models.ProbeSetting
                        {
                            TankNo = GetStr(p, "tankNo"),
                            ProbeId = GetStr(p, "probeId"),
                            IsDensityProbe = GetStr(p, "probeType") == "1",
                            OilOffsetMm = double.TryParse(GetStr(p, "oilOffset"), out double oo) ? oo : 0,
                            WaterOffsetMm = double.TryParse(GetStr(p, "waterOffset"), out double wo) ? wo : 0,
                            OilBlindMm = double.TryParse(GetStr(p, "oilBlind"), out double ob) ? ob : 0,
                            HighWarningMm = double.TryParse(GetStr(p, "highWarning"), out double hw) ? hw : 0,
                            HighAlarmMm = double.TryParse(GetStr(p, "highAlarm"), out double ha) ? ha : 0,
                            LowWarningMm = double.TryParse(GetStr(p, "lowWarning"), out double lw) ? lw : 0,
                            LowAlarmMm = double.TryParse(GetStr(p, "lowAlarm"), out double la) ? la : 0,
                            WaterWarningMm = double.TryParse(GetStr(p, "waterWarning"), out double ww) ? ww : 0,
                            WaterAlarmMm = double.TryParse(GetStr(p, "waterAlarm"), out double wa) ? wa : 0,
                            HighTempC = double.TryParse(GetStr(p, "highTemp"), out double ht) ? ht : 0,
                            LowTempC = double.TryParse(GetStr(p, "lowTemp"), out double lt) ? lt : 0
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[ERROR] Prob datası parse edilərkən xəta: {ex.Message}", ConsoleColor.Red);
            }
            BackgroundLogger.Log("[UPLOAD] Cihaz prob ayarlarını yüklədi və yadda saxlanıldı", ConsoleColor.DarkGreen);
            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 12,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                msg         = (string?)null
            });
        }

        // ── TANK CƏDVƏLİ ──────────────────────────────────────────────
        [HttpPost("getTankVolData")]
        public IActionResult GetTankVolData([FromBody] JsonElement body)
        {
            string tankNo = "01";
            try { tankNo = body.GetProperty("data")
                               .GetProperty("tankNo").GetString() ?? "01"; } catch { }

            var entries = _store.TankTable
                .Where(e => e.TankNo == tankNo).ToList();

            BackgroundLogger.Log($"[REQUEST] Tank {tankNo} cədvəli tələb edildi", ConsoleColor.DarkCyan);

            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 13,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                data        = new
                {
                    tankNo  = tankNo,
                    volList = entries.Select(e => new
                    {
                        height = e.HeightMm.ToString(),
                        volume = e.VolumeLiters.ToString()
                    })
                },
                msg = (string?)null
            });
        }

        // ── SİZINTI SENSORU AYARLARI ───────────────────────────────────
        [HttpPost("getSensorSetData")]
        public IActionResult GetSensorSetData([FromBody] JsonElement body)
        {
            BackgroundLogger.Log("[REQUEST] Sensor ayarları tələb edildi", ConsoleColor.DarkCyan);

            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 15,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                data        = new
                {
                    sensorList = _store.Sensors.Select(s => new
                    {
                        sensorNo    = s.SensorNo,
                        sensorType  = s.SensorType,
                        position    = s.Position,
                        positionNum = s.PositionNum,
                        used        = s.Enabled ? "1" : "0"
                    }).ToList()
                },
                msg = (string?)null
            });
        }

        // ── YAĞ MƏHSULU AYARLARI ─────────────────────────────────────────
        [HttpPost("getOilData")]
        public IActionResult GetOilData([FromBody] JsonElement body)
        {
            BackgroundLogger.Log("[REQUEST] Yağ məhsulu ayarları tələb edildi", ConsoleColor.DarkCyan);

            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 7,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                data        = new
                {
                    oilList = _store.OilProducts.Select(o => new
                    {
                        oilCode       = o.OilCode,
                        oilName       = o.OilName,
                        oilColor      = o.OilColor,
                        oilRate       = o.ExpansionRate,
                        temperature   = o.Temperature,
                        weightDensity = o.WeightDensity
                    })
                },
                msg = (string?)null
            });
        }

        [HttpPost("uploadOilData")]
        public async Task<IActionResult> UploadOilData([FromBody] JsonElement body)
        {
            ValidateToken(body);
            try
            {
                if (body.TryGetProperty("data", out JsonElement dataEl) && dataEl.TryGetProperty("oilList", out JsonElement listEl))
                {
                    foreach (var o in listEl.EnumerateArray())
                    {
                        await _store.UpdateOilProductAsync(new WindbellTank.Models.OilProductSetting
                        {
                            OilCode = GetStr(o, "oilCode"),
                            OilName = GetStr(o, "oilName"),
                            OilColor = GetStr(o, "oilColor"),
                            ExpansionRate = GetStr(o, "oilRate"),
                            Temperature = GetStr(o, "temperature"),
                            WeightDensity = GetStr(o, "weightDensity")
                        });
                    }
                }
            }
            catch { }
            BackgroundLogger.Log("[UPLOAD] Cihaz yağ məhsulu ayarlarını yüklədi və yadda saxlanıldı", ConsoleColor.DarkGreen);
            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 8,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                msg         = (string?)null
            });
        }

        // ── SIXLIQ AYARLARI ──────────────────────────────────────────────
        [HttpPost("getDensityData")]
        public IActionResult GetDensityData([FromBody] JsonElement body)
        {
            BackgroundLogger.Log("[REQUEST] Sıxlıq ayarları tələb edildi", ConsoleColor.DarkCyan);

            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 17,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                data        = new
                {
                    densityList = _store.Densities.Select(d => new
                    {
                        tankNo         = d.TankNo,
                        heightD        = d.HeightDiff,
                        fixRate        = d.FixRate,
                        initDensity    = d.InitDensity,
                        secondDensity  = d.SecondDensity,
                        densityFloatNo = d.DensityFloatNo
                    })
                },
                msg = (string?)null
            });
        }

        [HttpPost("uploadDensityData")]
        public async Task<IActionResult> UploadDensityData([FromBody] JsonElement body)
        {
            ValidateToken(body);
            try
            {
                if (body.TryGetProperty("data", out JsonElement dataEl) && dataEl.TryGetProperty("densityList", out JsonElement listEl))
                {
                    foreach (var d in listEl.EnumerateArray())
                    {
                        await _store.UpdateDensityAsync(new WindbellTank.Models.DensitySetting
                        {
                            TankNo = GetStr(d, "tankNo"),
                            HeightDiff = GetStr(d, "heightD"),
                            FixRate = GetStr(d, "fixRate"),
                            InitDensity = GetStr(d, "initDensity"),
                            SecondDensity = GetStr(d, "secondDensity"),
                            DensityFloatNo = GetStr(d, "densityFloatNo")
                        });
                    }
                }
            }
            catch { }
            BackgroundLogger.Log("[UPLOAD] Cihaz sıxlıq ayarlarını yüklədi və yadda saxlanıldı", ConsoleColor.DarkGreen);
            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 18,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                msg         = (string?)null
            });
        }

        // ── YANACAQ QAZ SENSORU AYARLARI ─────────────────────────────────
        [HttpPost("getGasSetData")]
        public IActionResult GetGasSetData([FromBody] JsonElement body)
        {
            BackgroundLogger.Log("[REQUEST] Qaz sensoru ayarları tələb edildi", ConsoleColor.DarkCyan);

            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 21,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                data        = new
                {
                    gasList = _store.GasSensors.Select(g => new
                    {
                        sensorNo    = g.SensorNo,
                        position    = g.Position,
                        positionNum = g.PositionNum,
                        used        = g.Enabled ? "1" : "0"
                    })
                },
                msg = (string?)null
            });
        }

        [HttpPost("uploadGasSetData")]
        public async Task<IActionResult> UploadGasSetData([FromBody] JsonElement body)
        {
            ValidateToken(body);
            try
            {
                if (body.TryGetProperty("data", out JsonElement dataEl) && dataEl.TryGetProperty("gasList", out JsonElement listEl))
                {
                    foreach (var g in listEl.EnumerateArray())
                    {
                        await _store.UpdateGasSensorAsync(new WindbellTank.Models.GasSensorSetting
                        {
                            SensorNo = GetStr(g, "sensorNo"),
                            Position = GetStr(g, "position"),
                            PositionNum = GetStr(g, "positionNum"),
                            Enabled = GetStr(g, "used") == "1"
                        });
                    }
                }
            }
            catch { }
            BackgroundLogger.Log("[UPLOAD] Cihaz qaz sensoru ayarlarını yüklədi və yadda saxlanıldı", ConsoleColor.DarkGreen);
            return Ok(new
            {
                code        = 200,
                result      = 0,
                commandType = 22,
                serverTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                msg         = (string?)null
            });
        }

        // ── REAL-TIME DATA — Cihazdan məlumat gəlir ───────────────────
        [HttpPost("uploadAtgData")]
        public IActionResult UploadAtgData([FromBody] JsonElement body)
        {
            try
            {
                var data = body.GetProperty("data");

                // İdentifikasiya
                string iotDevID = GetStr(data, "iotDevID");
                string tankNoRaw = GetStr(data, "tankNo");
                string tankNo = tankNoRaw != "-" ? tankNoRaw.PadLeft(2, '0') : "01"; // "2" -> "02" formatına sal
                string oilCode = GetStr(data, "oilCode");
                string oilName = GetStr(data, "oilName");

                // Auto-Discovery Xəta Emalı: Çən proqramda tapılmadıqda konfiqurasiyanı yenidən sorğula
                var existingTank = _store.Tanks.FirstOrDefault(t => t.TankNo == tankNo && t.Enabled);
                if (existingTank == null)
                {
                    BackgroundLogger.Log($"[AUTO-DISCOVERY] TANK_NOT_FOUND: Çən {tankNo} aktiv deyil və ya tapılmadı. Cihazın 'uploadTankData' göndərməsi gözlənilir...", ConsoleColor.Yellow);
                }

                // Səviyyə məlumatları
                string totalH = GetStr(data, "totalH");
                string waterH = GetStr(data, "waterH");
                string oilVt = GetStr(data, "oilVt");
                string waterVt = GetStr(data, "waterVt");
                string ullage = GetStr(data, "ullage");

                // Temperatur
                string oilT = GetStr(data, "oilT");
                string t1 = GetStr(data, "t1");
                string t2 = GetStr(data, "t2");
                string t3 = GetStr(data, "t3");
                string t4 = GetStr(data, "t4");

                // Həcm Kompensasiyası
                string oilV20 = GetStr(data, "oilV20");
                string totalV20 = GetStr(data, "totalV20");

                // Status - Cihazdan gələn probeValve statusları
                string probeValveCode = GetStr(data, "probeValve");
                string probeValveText = probeValveCode switch
                {
                    "1" => "Normal",
                    "4" => "Xəta",
                    "6" => "Kəsilmə (Siqnal yoxdur)",
                    _ => $"Bilinmir ({probeValveCode})"
                };

                // Digər
                string density = GetStr(data, "density");
                string weight = GetStr(data, "weight");
                string rawTime = GetStr(data, "uploadTime");
                
                string formattedTime = rawTime;
                if (DateTime.TryParse(rawTime, out DateTime parsedTime))
                {
                    formattedTime = parsedTime.ToString("yyyy-MM-dd HH:mm:ss");
                }
                else if (!string.IsNullOrEmpty(rawTime) && rawTime.Length == 14 && long.TryParse(rawTime, out _))
                {
                    if (DateTime.TryParseExact(rawTime, "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime exactParsed))
                    {
                        formattedTime = exactParsed.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine("\n=========================================");
                sb.AppendLine($"[REAL-TIME DATA] Yüklənmə Vaxtı: {formattedTime}");
                
                sb.AppendLine("--- İdentifikasiya ---");
                sb.AppendLine($"Cihaz ID: {iotDevID} | Çən №: {tankNo} | Məhsul: {oilCode} ({oilName})");
                
                sb.AppendLine("--- Səviyyə Məlumatları ---");
                sb.AppendLine($"Ümumi hündürlük: {totalH} mm | Su səviyyəsi: {waterH} mm");
                sb.AppendLine($"Xalis yanacaq həcmi: {oilVt} L | Su həcmi: {waterVt} L | Boş qalan həcm (Ullage): {ullage} L");

                sb.AppendLine("--- Temperatur ---");
                sb.AppendLine($"Ortalama Temp: {oilT} °C (T1: {t1}, T2: {t2}, T3: {t3}, T4: {t4})");

                sb.AppendLine("--- Həcm Kompensasiyası ---");
                sb.AppendLine($"V20 Standart Həcm: {oilV20} L | Ümumi Standart Həcm: {totalV20} L");

                sb.AppendLine("--- Status ---");
                sb.AppendLine($"Zond Statusu: {probeValveText}");

                sb.AppendLine("--- Digər ---");
                sb.AppendLine($"Sıxlıq: {density} kg/m³ | Çəki: {weight} kq");
                sb.AppendLine("=========================================");

                BackgroundLogger.LogBlock(sb.ToString(), ConsoleColor.Yellow);
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[ERROR] UploadAtgData parse edilərkən xəta: {ex.Message}", ConsoleColor.Red);
            }

            return Ok(new { code = 200, result = 0, commandType = 1, serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), msg = (string?)null });
        }

        // ── ALARM DATA ─────────────────────────────────────────────────
        [HttpPost("uploadAtgAlarmData")]
        public IActionResult UploadAlarmData([FromBody] JsonElement body)
        {
            ValidateToken(body);
            BackgroundLogger.Log("[⚠️ ATG ALARM] " + body.ToString(), ConsoleColor.Red);
            return Ok(new { code=200, result=0, commandType=3, serverTime=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), msg=(string?)null });
        }

        [HttpPost("uploadDeviceAlarmData")]
        public IActionResult UploadDeviceAlarmData([FromBody] JsonElement body)
        {
            ValidateToken(body);
            BackgroundLogger.Log("[⚠️ DEVICE ALARM] " + body.ToString(), ConsoleColor.Red);
            return Ok(new { code=200, result=0, commandType=4, serverTime=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), msg=(string?)null });
        }

        [HttpPost("uploadSensorData")]
        public IActionResult UploadSensorData([FromBody] JsonElement body)
        {
            ValidateToken(body);
            BackgroundLogger.Log("[⚠️ LEAK SENSOR ALARM] Sızıntı detektorundan məlumat gəldi:\n" + body.ToString(), ConsoleColor.Red);

            try
            {
                if (body.TryGetProperty("data", out JsonElement dataEl) && dataEl.TryGetProperty("sensorList", out JsonElement listEl))
                {
                    foreach (var s in listEl.EnumerateArray())
                    {
                        string status = GetStr(s, "status");
                        string statusName = status switch
                        {
                            "0" => "Normal",
                            "1" => "Su / Water",
                            "5" => "Sızıntı / Leakage",
                            _ => $"Bilinməyən xəta ({status})"
                        };
                        string position = GetStr(s, "position");
                        string sensorNo = GetStr(s, "sensorNo");

                        BackgroundLogger.Log($"[SENSOR DATA] Sensor No: {sensorNo}, Yer: {position}, Status: {statusName}", 
                            status == "0" ? ConsoleColor.DarkGreen : ConsoleColor.Yellow);
                    }
                }
            }
            catch (Exception ex)
            {
                BackgroundLogger.Log($"[ERROR] Sensor datası parse edilərkən xəta: {ex.Message}", ConsoleColor.Red);
            }

            return Ok(new { code=200, result=0, commandType=2, serverTime=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), msg=(string?)null });
        }

        [HttpPost("uploadDeliveryData")]
        public IActionResult UploadDeliveryData([FromBody] JsonElement body)
        {
            ValidateToken(body);
            BackgroundLogger.Log("[DELIVERY DATA] Yanacaq qəbulu (Delivery) məlumatı gəldi.", ConsoleColor.Green);
            int commandType = 5; // Default for Delivery Data
            if (body.TryGetProperty("commandType", out JsonElement cmdEl))
            {
                if (cmdEl.ValueKind == JsonValueKind.Number) commandType = cmdEl.GetInt32();
                else if (cmdEl.ValueKind == JsonValueKind.String && int.TryParse(cmdEl.GetString(), out int c)) commandType = c;
            }
            return Ok(new { code=200, result=0, commandType=commandType, serverTime=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), msg=(string?)null });
        }

        // Helpers
        private string GetIotDevId(JsonElement body)
        {
            try { return body.GetProperty("data").GetProperty("iotDevID").GetString() ?? "unknown"; }
            catch { return "unknown"; }
        }
        private string GetStr(JsonElement el, string key)
        {
            try 
            { 
                var prop = el.GetProperty(key);
                if (prop.ValueKind == JsonValueKind.String)
                    return prop.GetString() ?? "-";
                else if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetRawText();
                else
                    return prop.ToString() ?? "-";
            } 
            catch { return "-"; }
        }

        private void ValidateToken(JsonElement body)
        {
            try
            {
                if (body.TryGetProperty("token", out JsonElement tokenEl))
                {
                    string appId = GetStr(tokenEl, "appId");

                    string time = GetStr(tokenEl, "time");
                    if (time == "-") time = GetStr(tokenEl, "timestamp");
                    string sign = GetStr(tokenEl, "sign");
                    
                    string dataBlockStr = "{}";
                    if (body.TryGetProperty("data", out JsonElement dataEl))
                    {
                        dataBlockStr = Crc16Helper.RemoveFormattingSpaces(dataEl.GetRawText());
                    }
                    
                    string calcSign = Crc16Helper.CalculateModbusCrc16(dataBlockStr);
                    bool match = string.Equals(sign, calcSign, StringComparison.OrdinalIgnoreCase);
                    
                    BackgroundLogger.Log($"[TOKEN_DEBUG] Gələn: appId='{appId}', sign='{sign}', time='{time}'. Server time='{DateTime.Now:yyyy-MM-dd HH:mm:ss}'. Hesablanan: '{calcSign}' — {(match ? "✔ UYĞUNDUR" : "✘ UYĞUN DEYİL")}", ConsoleColor.DarkGray);
                }
            }
            catch { /* Token yoxdursa və ya xətalıdırsa davam et */ }
        }

        private string GetAppId(JsonElement body)
        {
            try { return body.GetProperty("token").GetProperty("appId").GetString() ?? "Server"; }
            catch { return "Server"; }
        }

        // (Removed old CalculateModbusCrc16 methods as they are now in Crc16Helper)
        // ── SİNXRONİZASİYA (Versiya Müqayisəsi) ────────────────────────
        // Windbell protokolunda cihaz heartbeat göndərir, server versiyaları qaytarır.
        // Versiya fərqi olanda cihaz özü upload*/get* endpointlərini çağırır.
        // Biz cihazı çağırmırıq — cihaz API server deyil, yalnız client-dir.
        private void CompareVersionsAndLog(JsonElement body)
        {
            try
            {
                if (!body.TryGetProperty("data", out JsonElement data)) return;

                int deviceSensorVer = int.TryParse(GetStr(data, "sensorVer"), out int sv) ? sv : 0;
                int deviceGasVer = int.TryParse(GetStr(data, "gasVer"), out int gv) ? gv : 0;

                var diffs = new System.Collections.Generic.List<string>();
                bool requiresTankSync = false;
                bool requiresSensorSync = false;
                bool requiresGasSync = false;

                if (data.TryGetProperty("tankList", out JsonElement tankList))
                {
                    foreach (var t in tankList.EnumerateArray())
                    {
                        string tankNo = GetStr(t, "tankNo").PadLeft(2, '0');
                        int tVer = int.TryParse(GetStr(t, "tankVer"), out int tv) ? tv : 0;
                        int pVer = int.TryParse(GetStr(t, "probeVer"), out int pv) ? pv : 0;
                        int dVer = int.TryParse(GetStr(t, "densityVer"), out int dv) ? dv : 0;

                        if (tVer != _store.TankVer) 
                        {
                            diffs.Add($"Tank{tankNo}:v{tVer}≠{_store.TankVer}");
                            requiresTankSync = true;
                        }
                        if (pVer != _store.ProbeVer) diffs.Add($"Prob{tankNo}:v{pVer}≠{_store.ProbeVer}");
                        if (dVer != _store.DensityVer) diffs.Add($"Density{tankNo}:v{dVer}≠{_store.DensityVer}");
                    }
                }

                if (deviceSensorVer > _store.SensorVer) 
                {
                    diffs.Add($"Sensor:v{deviceSensorVer}>{_store.SensorVer}");
                    requiresSensorSync = true;
                }
                if (deviceGasVer > _store.GasVer) 
                {
                    diffs.Add($"Gas:v{deviceGasVer}>{_store.GasVer}");
                    requiresGasSync = true;
                }

                if (diffs.Count > 0)
                {
                    BackgroundLogger.Log($"[SYNC] Versiya fərqləri: {string.Join(", ", diffs)}", ConsoleColor.Yellow);
                    
                    if (requiresTankSync)
                    {
                        BackgroundLogger.Log($"[SYNC] Çən versiyası fərqlidir. Cihazın 'uploadTankData' və ya 'getTankData' çağırması gözlənilir...", ConsoleColor.Yellow);
                    }
                    if (requiresSensorSync)
                    {
                        BackgroundLogger.Log($"[SYNC] Sızıntı sensoru versiyası fərqlidir. Cihazın 'uploadSensorData' çağırması gözlənilir...", ConsoleColor.Yellow);
                    }
                    if (requiresGasSync)
                    {
                        BackgroundLogger.Log($"[SYNC] Qaz sensoru versiyası fərqlidir. Cihazın 'uploadGasSetData' çağırması gözlənilir...", ConsoleColor.Yellow);
                    }
                }
            }
            catch { }
        }
    }
}
