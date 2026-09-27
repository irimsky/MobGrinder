using System.Numerics;

using LuminaSupplemental.Excel.Model;

namespace MobGrinder;

/// <summary>
/// MobGrinder-only additions for locations missing from the embedded MobSpawn.csv.
/// Position is a map coordinate (X/Y), not a world coordinate; MobCoordinateService
/// converts it to world X/Z and vnavmesh resolves the live ground height later.
/// </summary>
internal static class MobSpawnSupplementData
{
    public static IReadOnlyList<MobSpawnPosition> Entries { get; } =
    [
        // 奥阔帕恰山 图拉尔蜜獾
        Create(17475, 13089, 1187, 30.34f, 15.73f),

        // 奥阔帕恰山 巨龙舌兰
        Create(17466, 13080, 1187, 18.42f, 13.61f),
        // 奥阔帕恰山 巨龙舌兰
        Create(17466, 13080, 1187, 22.06f, 14.05f),

        // 奥阔帕恰山 巨颚蜥
        Create(17482, 13096, 1187, 25.42f, 22.05f),

        // 奥阔帕恰山 其瓦固雕工
        Create(17525, 13099, 1187, 21.42f, 35.09f),

        // 克扎玛乌卡湿地 呜噜怪
        Create(17230, 12931, 1188, 16.46f, 5.97f),

        // 克扎玛乌卡湿地 豹猫
        Create(17239, 12941, 1188, 35.54f, 13.66f),

        // 克扎玛乌卡湿地 纸巢胡蜂
        Create(17245, 12949, 1188, 35.74f, 35.73f),

        // 克扎玛乌卡湿地 小亚波伦
        Create(17227, 12943, 1188, 9.34f, 21.93f),

        // 亚克特尔树海 长牙狞豹
        Create(17255, 12956, 1189, 13.06f, 8.97f),

        // 亚克特尔树海 土石之翼
        Create(17259, 12960, 1189, 34.74f, 13.77f),
        // 亚克特尔树海 土石之翼
        Create(17259, 12960, 1189, 30.42f, 17.65f),

        // 亚克特尔树海 拟鸟枝
        Create(17251, 12964, 1189, 14.78f, 25.73f),
        // 亚克特尔树海 拟鸟枝
        Create(17251, 12964, 1189, 20.02f, 24.33f),

        // 亚克特尔树海 蓝叶灵
        Create(17254, 12966, 1189, 8.22f, 26.65f),

        // 夏劳尼荒野 风滚蟹
        Create(17269, 12972, 1190, 33.22f, 29.01f),
        // 夏劳尼荒野 风滚蟹
        Create(17269, 12972, 1190, 31.14f, 33.01f),

        // 夏劳尼荒野 角盗龙
        Create(17272, 12989, 1190, 27.38f, 13.33f),
        // 夏劳尼荒野 角盗龙
        Create(17272, 12989, 1190, 32.58f, 19.97f),
        // 夏劳尼荒野 角盗龙
        Create(17272, 12989, 1190, 28.94f, 17.21f),

        // 夏劳尼荒野 犎牛
        Create(17275, 12991, 1190, 22.34f, 10.97f),

        // 夏劳尼荒野 圆扇刺
        Create(17270, 12973, 1190, 16.42f, 16.89f),
        // 夏劳尼荒野 圆扇刺
        Create(17270, 12973, 1190, 14.30f, 27.41f),
        // 夏劳尼荒野 圆扇刺
        Create(17270, 12973, 1190, 16.10f, 21.29f),
        // 夏劳尼荒野 圆扇刺
        Create(17270, 12973, 1190, 24.46f, 34.37f),

        // 遗产之地 引导之牙
        Create(17500, 13116, 1191, 27.66f, 26.17f),
        // 遗产之地 引导之牙
        Create(17500, 13116, 1191, 28.82f, 22.65f),

        // 遗产之地 卡托布莱普塔
        Create(17497, 13113, 1191, 35.42f, 12.01f),

        // 遗产之地 嵌齿象
        Create(17488, 13104, 1191, 13.94f, 16.13f),

        // 遗产之地 鬃背兽
        Create(17494, 13110, 1191, 17.58f, 33.89f),

        // 活着的记忆 飞天猫
        Create(17505, 13121, 1192, 33.22f, 35.33f),
        // 活着的记忆 飞天猫
        Create(17505, 13121, 1192, 33.54f, 32.17f),

        // 活着的记忆 火绳蝎
        Create(17513, 13129, 1192, 26.50f, 6.61f),

        // 活着的记忆 永恒杉树精
        Create(17520, 13136, 1192, 17.74f, 22.73f),

        // 活着的记忆 液态灵魂
        Create(17508, 13124, 1192, 10.38f, 36.57f),
    ];

    private static MobSpawnPosition Create(
        uint bNpcBaseId,
        uint bNpcNameId,
        uint territoryTypeId,
        float mapX,
        float mapY) => new()
        {
            BNpcBaseId = bNpcBaseId,
            BNpcNameId = bNpcNameId,
            TerritoryTypeId = territoryTypeId,
            Position = new Vector3(mapX, mapY, 0f),
            Subtype = 0,
        };
}
