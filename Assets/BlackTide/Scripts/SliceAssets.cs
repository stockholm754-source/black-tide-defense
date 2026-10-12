using UnityEngine;

namespace BlackTide
{
    // 运行时需要的资产引用，由编辑器脚本 BlackTide/Setup 自动填好，放在 Resources 里
    public class SliceAssets : ScriptableObject
    {
        public GameObject heroModel, bossModel, minionModel, axeModel, bladeModel;
        public RuntimeAnimatorController heroController, bossController, minionController;
        public Texture2D heroTexture, skeletonTexture;
        public Texture2D groundAlbedo, groundNormal, tideNoise, softDot, slashStreak;
        public Shader additiveShader, telegraphShader, tideShader;
        // Tripo 新模型（为空时自动退回 KayKit 占位模型）
        public GameObject heroTripo, knightHumanoid;
        public RuntimeAnimatorController heroTripoController;
        public Texture2D heroTripoAlbedo, heroTripoEmission;
        public GameObject bossTripo, minionTripo;
        public RuntimeAnimatorController bossTripoController, minionTripoController;
        public Texture2D bossTripoAlbedo, bossTripoEmission, minionTripoAlbedo, minionTripoEmission;
        public GameObject swordTripo, axeTripo;
        public Texture2D swordTripoAlbedo, swordTripoEmission, axeTripoAlbedo, axeTripoEmission;
        // 新场景：烘焙好的整张地面 + 原始地砖（给道具和崖壁用）
        public Texture2D groundBake, groundBakeNormal, groundBakeEmission, floorTile;
        // 第五批：开放地图地面（Art/Ground，Tools/ground_textures.py 处理过：去接缝、1024）
        public Texture2D gDirt, gMoss, gSand, gCobble, gTide, gTideEm, gNormal;
        // 场景道具（Tripo），模型为空时竞技场退回占位几何体
        public PropAsset pillarBroken, pillarFallen, brazier, tideCrystal, wallRuin, statueHooded;
        // 青岚、澄音（Tripo，人形 + Mixamo；为空时用 KayKit 骑士换色占位）、青岚的手枪、澄音的音感仪
        public GameObject qinglanTripo, chengyinTripo, pistolTripo, instrumentTripo;
        // 第三批：C1 重甲潮兵（巨浪）、C2 黑潮信使 + 29 长戟、C3 商人、C4 探员（为空时退回旧的顶替方案）
        public GameObject bruteTripo, messengerTripo, merchantTripo, scoutTripo, glaiveTripo;
        public Texture2D bruteAlbedo, bruteEmission, messengerAlbedo, messengerEmission, merchantAlbedo, scoutAlbedo, glaiveAlbedo, glaiveEmission;
        public Texture2D qinglanAlbedo, chengyinAlbedo, pistolAlbedo, instrumentAlbedo;
        // A3 地图道具（Art/Map/<编号>_<名字>），按 id 查
        public System.Collections.Generic.List<PropAsset> mapProps = new System.Collections.Generic.List<PropAsset>();
        public PropAsset MapProp(string id) { foreach (var p in mapProps) if (p != null && p.id == id) return p; return null; }
        // 第四批（敌人）：C5–C13 九个人形敌人 + 59–63 五把武器，按名字查（Art/Tripo/Batch4/<名字>）。54/56/58 在 mapProps 里
        public System.Collections.Generic.List<PropAsset> batch4 = new System.Collections.Generic.List<PropAsset>();
        public PropAsset B4(string id) { foreach (var p in batch4) if (p != null && p.id == id) return p; return null; }

        static SliceAssets _i;
        public static SliceAssets I
        {
            get
            {
                if (_i == null) _i = Resources.Load<SliceAssets>("SliceAssets");
                return _i;
            }
        }
    }

    [System.Serializable]
    public class PropAsset
    {
        public string id;
        public GameObject model;
        public Texture2D albedo, emission;
        public bool Ok => model != null;
    }
}
