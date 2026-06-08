using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WindbellTank.Services
{
    /// <summary>
    /// Windbell SS Series ATG Console üçün HTTP/JSON Client.
    /// Modbus CRC16 hesablama, token generasiyası və endpoint metodlarını cəmləyir.
    /// </summary>
    public class WindbellApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly DeviceSettingsStore _store;

        public WindbellApiClient(HttpClient httpClient, DeviceSettingsStore store)
        {
            _httpClient = httpClient;
            _store = store;
        }

        /// <summary>
        /// Request üçün token yaradır.
        /// Crc16Helper istifadə olunur — imza kiçik hərflərlə, Little-Endian HEX formatında olur.
        /// </summary>
        /// 
        // private object GenerateToken(string dataJson)
        // {
        //     string appIdForToken = _store.AppId;

        //     string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        //     // Crc16Helper ilə boşluqları düzgün silirik (string dəyərlərin içindəki boşluqları qoruyaraq)
        //     string dataWithoutSpaces = Crc16Helper.RemoveFormattingSpaces(dataJson);
        //     // Kiçik hərfli, Little-Endian HEX formatında CRC16 imza
        //     string sign = Crc16Helper.CalculateModbusCrc16(dataWithoutSpaces);

        //     return new
        //     {
        //         appId = appIdForToken,
        //         timestamp = timestamp,
        //         sign = sign
        //     };
        // }

        /// <summary>
        /// Ümumi POST metodu
        /// </summary>
        private async Task<string> PostAsync<TData>(string endpoint, TData data, int expectedCommandType)
        {
            var options = new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = null, //kohne versiya-JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            string dataJson = JsonSerializer.Serialize(data, options);
            
            string appIdForToken = _store.AppId;
            
            // Cihaz token içində `timestamp` olaraq UNIX Timestamp (saniyə) gözləyir
            long timestamp = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds();
            string dataWithoutSpaces = Crc16Helper.RemoveFormattingSpaces(dataJson);
            string sign = Crc16Helper.CalculateModbusCrc16(dataWithoutSpaces);

            var tokenBlock = new
            {
                appId = appIdForToken,
                timestamp = timestamp.ToString(),
                sign = sign
            };
            
            var payload = new
            {
                data = data,
                token = tokenBlock,
            };

            string payloadJson = JsonSerializer.Serialize(payload, options);
            BackgroundLogger.Log($"[API_POST] URL={endpoint}, data={dataJson}, sign={sign}", ConsoleColor.DarkYellow);
            
            var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, content);
            response.EnsureSuccessStatusCode();

            string responseString = await response.Content.ReadAsStringAsync();
            
            // Cavabın yoxlanılması (code = 200, result = 0)
            using JsonDocument doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            int code = root.TryGetProperty("code", out var codeEl) ? codeEl.GetInt32() : -1;
            int result = root.TryGetProperty("result", out var resEl) ? resEl.GetInt32() : -1;
            int commandType = root.TryGetProperty("commandType", out var cmdEl) ? cmdEl.GetInt32() : -1;

            if (code != 200 || result != 0)
            {
                string msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() ?? "Bilinməyən xəta baş verdi" : "Bilinməyən xəta baş verdi";
                throw new Exception($"Cihaz API xətası (Endpoint: {endpoint}): Code={code}, Result={result}, Mesaj={msg}");
            }

            if (expectedCommandType > 0 && commandType != expectedCommandType)
            {
                throw new Exception($"Cihaz API xətası: Gözlənilməyən commandType alındı. Gözlənilən: {expectedCommandType}, Alınan: {commandType}");
            }

            return responseString;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Endpoints
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        // Heartbeat & Sync: /deviceAPI/getSysVersionData
        // Mütləq serverTime qaytarmalıdır.
        /// </summary>
        public async Task<string> SyncHeartbeatAsync(object deviceData)
        {
            return await PostAsync("/deviceAPI/getSysVersionData", deviceData, 6);
        }

        /// <summary>
        // Tank Management: /deviceAPI/uploadTankData
        // (diameter, volume, used='1' və ya '0')
        /// </summary>
        public async Task<string> UploadTankDataAsync(object tankData)
        {
            return await PostAsync("/deviceAPI/uploadTankData", tankData, 10);
        }

        /// <summary>
        // Probe Configuration: /deviceAPI/uploadProbeData
        // (Probe ID, offsets, alarms kimi dəyərlər)
        /// </summary>
        public async Task<string> UploadProbeDataAsync(object probeData)
        {
            return await PostAsync("/deviceAPI/uploadProbeData", probeData, 12);
        }

        /// <summary>
        // Tank Table (Calibration): /deviceAPI/uploadTankVolData
        // (Hündürlük - həcm cədvəli)
        /// </summary>
        public async Task<string> UploadTankVolDataAsync(object tankTableData)
        {
            return await PostAsync("/deviceAPI/uploadTankVolData", tankTableData, 14); // Sənədə uyğun olaraq commandType dəyişə bilər
        }

        /// <summary>
        // Sensor Setup: /deviceAPI/uploadSensorSetData
        // (Sızıntı sensorları)
        /// </summary>
        public async Task<string> UploadSensorSetDataAsync(object sensorData)
        {
            return await PostAsync("/deviceAPI/uploadSensorSetData", sensorData, 16); // Sənədə uyğun olaraq commandType dəyişə bilər
        }

        /// <summary>
        // Oil Product: /deviceAPI/uploadOilData
        /// </summary>
        public async Task<string> UploadOilProductDataAsync(object oilData)
        {
            return await PostAsync("/deviceAPI/uploadOilData", oilData, 8); 
        }

        /// <summary>
        // Density: /deviceAPI/uploadDensityData
        /// </summary>
        public async Task<string> UploadDensityDataAsync(object densityData)
        {
            return await PostAsync("/deviceAPI/uploadDensityData", densityData, 18); 
        }

        /// <summary>
        // Gas Sensor: /deviceAPI/uploadGasSensorData
        /// </summary>
        public async Task<string> UploadGasSensorDataAsync(object gasSensorData)
        {
            return await PostAsync("/deviceAPI/uploadGasSensorData", gasSensorData, 22); 
        }
    }
}
