namespace MuConvert.utils;

public enum LaneType
{
    Red,
    Green,
    Blue,
    Colorful, // 多彩线，颜色详见ColorfulLaneColor枚举
    Wall, // 墙壁，即场地的左右边界
}

public enum ColorfulLaneColor
{
    Akari = 0,
    Yuzu = 1,
    Rio = 2,
    Riku = 3,
    Tsubaki = 4,
    Ayaka = 5,
    Kaede = 6,
    Saki = 7,
    Koboshi = 8,
    Arisu = 9,
    Mia = 10,
    Chinatsu = 11,
    Tsumugi = 12,
    Setsuna = 13,
    Brown = 14,
    Haruna = 15,
    Black = 16,
    Akane = 17,
    G = 18,
    Aoi = 19,
}

public enum Direction { L, R }

public enum BulletSize { N, L } // Normal or Large

public enum BulletType
{
    CIR, // 圆形子弹
    NDL, // 针状子弹
    SQR, // 圆柱形(方状)子弹
}

public enum BulletShooter
{ 
    UPS, // 从具体BLT命令行中所指定的那个Pos位置
    ENE, // 从敌人位置
    CEN, // 从谱面中心即Pos=0的位置
}

public enum BulletDamage
{ 
    NML, // Normal 使用BULLET_DAMAGE伤害
    STR, // Hard 使用HARDBULLET_DAMAGE伤害
    DNG, // Danger 使用DANGERBULLET_DAMAGE伤害
}
