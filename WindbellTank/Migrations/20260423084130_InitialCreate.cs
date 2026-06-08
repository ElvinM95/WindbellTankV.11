using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WindbellTank.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WindbellDensitySettings",
                columns: table => new
                {
                    TankNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    HeightDiff = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FixRate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InitDensity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SecondDensity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DensityFloatNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Remark = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WindbellDensitySettings", x => x.TankNo);
                });

            migrationBuilder.CreateTable(
                name: "WindbellGasSensorSettings",
                columns: table => new
                {
                    SensorNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PositionNum = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WindbellGasSensorSettings", x => x.SensorNo);
                });

            migrationBuilder.CreateTable(
                name: "WindbellOilProductSettings",
                columns: table => new
                {
                    OilCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OilName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OilColor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExpansionRate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Temperature = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WeightDensity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WindbellOilProductSettings", x => x.OilCode);
                });

            migrationBuilder.CreateTable(
                name: "WindbellProbeSettings",
                columns: table => new
                {
                    TankNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ProbeId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsDensityProbe = table.Column<bool>(type: "bit", nullable: false),
                    OilOffsetMm = table.Column<double>(type: "float", nullable: false),
                    WaterOffsetMm = table.Column<double>(type: "float", nullable: false),
                    OilBlindMm = table.Column<double>(type: "float", nullable: false),
                    HighWarningMm = table.Column<double>(type: "float", nullable: false),
                    HighAlarmMm = table.Column<double>(type: "float", nullable: false),
                    LowWarningMm = table.Column<double>(type: "float", nullable: false),
                    LowAlarmMm = table.Column<double>(type: "float", nullable: false),
                    WaterWarningMm = table.Column<double>(type: "float", nullable: false),
                    WaterAlarmMm = table.Column<double>(type: "float", nullable: false),
                    HighTempC = table.Column<double>(type: "float", nullable: false),
                    LowTempC = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WindbellProbeSettings", x => new { x.TankNo, x.ProbeId });
                });

            migrationBuilder.CreateTable(
                name: "WindbellSensorSettings",
                columns: table => new
                {
                    SensorNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SensorType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PositionNum = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WindbellSensorSettings", x => x.SensorNo);
                });

            migrationBuilder.CreateTable(
                name: "WindbellSystemVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TankVer = table.Column<int>(type: "int", nullable: false),
                    ProbeVer = table.Column<int>(type: "int", nullable: false),
                    SensorVer = table.Column<int>(type: "int", nullable: false),
                    TableVer = table.Column<int>(type: "int", nullable: false),
                    DensityVer = table.Column<int>(type: "int", nullable: false),
                    OilProductVer = table.Column<int>(type: "int", nullable: false),
                    GasVer = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WindbellSystemVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WindbellTankSettings",
                columns: table => new
                {
                    TankNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    OilCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OilName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DiameterMm = table.Column<int>(type: "int", nullable: false),
                    VolumeLiters = table.Column<int>(type: "int", nullable: false),
                    ExpansionRate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WindbellTankSettings", x => x.TankNo);
                });

            migrationBuilder.CreateTable(
                name: "WindbellTankTableEntries",
                columns: table => new
                {
                    TankNo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    HeightMm = table.Column<int>(type: "int", nullable: false),
                    VolumeLiters = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WindbellTankTableEntries", x => new { x.TankNo, x.HeightMm });
                });

            migrationBuilder.InsertData(
                table: "WindbellSystemVersions",
                columns: new[] { "Id", "DensityVer", "GasVer", "OilProductVer", "ProbeVer", "SensorVer", "TableVer", "TankVer" },
                values: new object[] { 1, 1, 1, 1, 1, 1, 1, 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WindbellDensitySettings");

            migrationBuilder.DropTable(
                name: "WindbellGasSensorSettings");

            migrationBuilder.DropTable(
                name: "WindbellOilProductSettings");

            migrationBuilder.DropTable(
                name: "WindbellProbeSettings");

            migrationBuilder.DropTable(
                name: "WindbellSensorSettings");

            migrationBuilder.DropTable(
                name: "WindbellSystemVersions");

            migrationBuilder.DropTable(
                name: "WindbellTankSettings");

            migrationBuilder.DropTable(
                name: "WindbellTankTableEntries");
        }
    }
}
