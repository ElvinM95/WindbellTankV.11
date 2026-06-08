using System;
using System.Text;
using System.Text.Json;

namespace WindbellTank.Services
{
    public static class Crc16Helper
    {
        /// <summary>
        /// Serializes an object to a JSON string with CamelCase naming policy
        /// and removes all whitespace characters to meet the strict Modbus CRC16 signature requirements.
        /// </summary>
        public static string SerializeAndRemoveSpaces(object data)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null, //kohne versiya - JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            string json = JsonSerializer.Serialize(data, options);
            return RemoveFormattingSpaces(json);
        }

        /// <summary>
        /// Safely removes spaces, newlines, and tabs from a JSON string,
        /// but preserves any spaces inside string values (like timestamps or names).
        /// </summary>
        public static string RemoveFormattingSpaces(string json)
        {
            if (string.IsNullOrEmpty(json)) return string.Empty;

            bool inQuotes = false;
            bool escape = false;
            var sb = new StringBuilder(json.Length);

            foreach (char c in json)
            {
                if (escape)
                {
                    sb.Append(c);
                    escape = false;
                    continue;
                }
                if (c == '\\')
                {
                    sb.Append(c);
                    escape = true;
                    continue;
                }
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    sb.Append(c);
                    continue;
                }
                if (!inQuotes && (c == ' ' || c == '\n' || c == '\r' || c == '\t'))
                {
                    continue; // Skip formatting whitespace
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Calculates the Modbus CRC16 hash for a given string (UTF-8) and returns it as a lowercase hex string (little-endian).
        /// </summary>
        public static string CalculateModbusCrc16(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            
            byte[] data = Encoding.UTF8.GetBytes(input);
            ushort crc = 0xFFFF;
            
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            // Bu hissə imzanı cihazın ekranında gördüyümüz '3301' formatına salır
            // BU HİSSƏ ƏN VACİBDİR:
            // Baytların yerini fırladırıq (Byte Swap), çünki log göstərir ki, 
            // cihaz '99af' yox, 'af99' formatında (Big-Endian) imza gözləyir.
            ushort swappedCrc = (ushort)((crc << 8) | (crc >> 8));
            
            return swappedCrc.ToString("x4");
        }

        /// <summary>
        /// Helper to generate a signature for a given data object.
        /// </summary>
        public static string GenerateSignature(object data)
        {
            string cleanJson = SerializeAndRemoveSpaces(data);
            return CalculateModbusCrc16(cleanJson);
        }
    }
}
