using System.Collections.Generic;

namespace WindbellTank.Models
{
    public class TankSetting
    {
        public string TankNo        { get; set; } = string.Empty;
        public string Version       { get; set; } = "1";
        public string OilCode       { get; set; } = string.Empty;
        public string OilName       { get; set; } = string.Empty;
        public int    DiameterMm    { get; set; }
        public int    VolumeLiters  { get; set; }
        public string ExpansionRate { get; set; } = "0.0012";
        public bool   Enabled       { get; set; } = true;
    }

    public class ProbeSetting
    {
        public string TankNo        { get; set; } = string.Empty;
        public string ProbeId       { get; set; } = string.Empty;
        public string Version       { get; set; } = "1";
        public bool   IsDensityProbe { get; set; }
        public double OilOffsetMm   { get; set; }
        public double WaterOffsetMm { get; set; }
        public double OilBlindMm    { get; set; }
        public double HighWarningMm { get; set; }
        public double HighAlarmMm   { get; set; }
        public double LowWarningMm  { get; set; }
        public double LowAlarmMm    { get; set; }
        public double WaterWarningMm { get; set; }
        public double WaterAlarmMm  { get; set; }
        public double HighTempC     { get; set; } = 55.0;
        public double LowTempC      { get; set; } = -40.0;
    }

    public class SensorSetting
    {
        public string SensorNo   { get; set; } = string.Empty;
        public string SensorType { get; set; } = "0";
        public string Position   { get; set; } = "0";
        public string PositionNum { get; set; } = "01";
        public bool   Enabled    { get; set; } = true;
    }

    public class OilProductSetting
    {
        public string OilCode       { get; set; } = string.Empty;  // "1020"
        public string OilName       { get; set; } = string.Empty;  // "92#"
        public string OilColor      { get; set; } = string.Empty;  // "red", "blue" və s.
        public string ExpansionRate { get; set; } = string.Empty;  // benzin: "0.0012", dizel: "0.0008"
        public string Temperature   { get; set; } = string.Empty;  // hesablama temperaturu
        public string WeightDensity { get; set; } = string.Empty;  // çəki sıxlığı
    }

    public class DensitySetting
    {
        public string TankNo          { get; set; } = string.Empty;  // "01"~"12"
        public string Version         { get; set; } = "1";
        public string HeightDiff      { get; set; } = string.Empty;  // hündürlük fərqi
        public string FixRate         { get; set; } = string.Empty;  // korreksiya əmsalı
        public string InitDensity     { get; set; } = string.Empty;  // başlanğıc sıxlığı
        public string SecondDensity   { get; set; } = string.Empty;  // ikinci sıxlıq
        public string DensityFloatNo  { get; set; } = string.Empty;  // sıxlıq üzən nömrəsi
        public string Remark          { get; set; } = string.Empty;
    }

    public class GasSensorSetting
    {
        public string SensorNo   { get; set; } = string.Empty;  // "01"~"64"
        // Mövqe: 0=yanacaq adası, 1=boşaltma ağzı,
        //        2=adam quyusu, 3=digər
        public string Position   { get; set; } = "0";
        public string PositionNum { get; set; } = "01";
        public bool   Enabled    { get; set; } = true;
    }

    public class TankTableEntry
    {
        public string TankNo      { get; set; } = string.Empty;
        public int    HeightMm    { get; set; }
        public int    VolumeLiters { get; set; }
    }
}
