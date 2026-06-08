using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WindbellTank.Models
{
    /// <summary>
    /// Bütün versiya nömrələrini saxlamaq üçün tək-sətirli cədvəl.
    /// Cihaz heartbeat-də versiyaları müqayisə edir.
    /// </summary>
    [Table("WindbellSystemVersions")]
    public class SystemVersion
    {
        [Key]
        public int Id { get; set; } = 1; // Həmişə 1 — tək sətir

        public int TankVer       { get; set; } = 1;
        public int ProbeVer      { get; set; } = 1;
        public int SensorVer     { get; set; } = 1;
        public int TableVer      { get; set; } = 1;
        public int DensityVer    { get; set; } = 1;
        public int OilProductVer { get; set; } = 1;
        public int GasVer        { get; set; } = 1;
    }
}
