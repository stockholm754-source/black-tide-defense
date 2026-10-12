using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BlackTide.EditorTools
{
    // Tripo 模型接入：
    // 1. 复制一份 KayKit 骑士，按人形（Humanoid）导入，手动指定骨骼映射，让它的动画变成通用的人形动画
    // 2. Tripo 导出的 Mixamo 骨骼模型按人形导入
    // 3. 两边都摆成 T 字参考姿势，否则动画迁移后手臂会偏
    // 4. 生成新的动画控制器，状态名和原来一样，战斗代码不用改
    public static class TripoSetup
    {
        const string Root = "Assets/BlackTide";
        const string KnightSrc = Root + "/Art/Characters/Knight.fbx";
        const string KnightHumanoid = Root + "/Art/Anim/KnightAnims_Humanoid.fbx";
        const string HeroFbx = Root + "/Art/Tripo/feilie.fbx";

        static readonly string[] LoopClips = { "Idle", "Idle_B", "Idle_Combat", "2H_Melee_Idle", "Unarmed_Idle", "Running_A", "Running_B", "Running_C",
            "Running_Strafe_Left", "Running_Strafe_Right", "Walking_A", "Walking_B", "Walking_C", "Walking_D_Skeletons", "Walking_Backwards", "Spellcasting", "Blocking", "2H_Melee_Attack_Spinning" };

        // KayKit Rig → Unity 人形骨骼
        static readonly Dictionary<string, string> KayKitMap = new Dictionary<string, string>
        {
            { "Hips", "hips" }, { "Spine", "spine" }, { "Chest", "chest" }, { "Head", "head" },
            { "LeftUpperLeg", "upperleg.l" }, { "LeftLowerLeg", "lowerleg.l" }, { "LeftFoot", "foot.l" }, { "LeftToes", "toes.l" },
            { "RightUpperLeg", "upperleg.r" }, { "RightLowerLeg", "lowerleg.r" }, { "RightFoot", "foot.r" }, { "RightToes", "toes.r" },
            { "LeftUpperArm", "upperarm.l" }, { "LeftLowerArm", "lowerarm.l" }, { "LeftHand", "wrist.l" },
            { "RightUpperArm", "upperarm.r" }, { "RightLowerArm", "lowerarm.r" }, { "RightHand", "wrist.r" },
        };

        [MenuItem("BlackTide/Tripo/Setup Hero (绯烈)")]
        public static void SetupHero()
        {
            BuildKnightHumanoid();
            SetupHumanoidModel(HeroFbx, null);
            BuildController(Root + "/Anim/Feilie.controller", KnightHumanoid, "2H_Melee_Idle");
            SetTexture(Root + "/Art/Tripo/feilie_albedo.png", true);
            SetTexture(Root + "/Art/Tripo/feilie_emission.png", true);

            string path = Root + "/Resources/SliceAssets.asset";
            var a = AssetDatabase.LoadAssetAtPath<SliceAssets>(path);
            a.heroTripo = AssetDatabase.LoadAssetAtPath<GameObject>(HeroFbx);
            a.heroTripoController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Root + "/Anim/Feilie.controller");
            a.heroTripoAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Tripo/feilie_albedo.png");
            a.heroTripoEmission = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Tripo/feilie_emission.png");
            a.knightHumanoid = AssetDatabase.LoadAssetAtPath<GameObject>(KnightHumanoid);
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log($"TRIPO_HERO_DONE model={(a.heroTripo != null)} ctrl={(a.heroTripoController != null)} tex={(a.heroTripoAlbedo != null)} em={(a.heroTripoEmission != null)}");
        }

        [MenuItem("BlackTide/Tripo/Setup Enemies (潮汐骑+残像)")]
        public static void SetupEnemies()
        {
            string warriorAnims = Root + "/Art/Anim/WarriorAnims_Humanoid.fbx";
            string minionAnims = Root + "/Art/Anim/MinionAnims_Humanoid.fbx";
            BuildHumanoidAnims(Root + "/Art/Characters/Skeleton_Warrior.fbx", warriorAnims);
            BuildHumanoidAnims(Root + "/Art/Characters/Skeleton_Minion.fbx", minionAnims);
            SetupHumanoidModel(Root + "/Art/Tripo/boss.fbx", null);
            SetupHumanoidModel(Root + "/Art/Tripo/minion.fbx", null);
            BuildController(Root + "/Anim/Boss_Tripo.controller", warriorAnims, "Idle_Combat");
            BuildController(Root + "/Anim/Minion_Tripo.controller", minionAnims, "Idle");
            foreach (var n in new[] { "boss_albedo", "boss_emission", "minion_albedo", "minion_emission" }) SetTexture(Root + "/Art/Tripo/" + n + ".png", true);

            var a = AssetDatabase.LoadAssetAtPath<SliceAssets>(Root + "/Resources/SliceAssets.asset");
            T L<T>(string rel) where T : Object => AssetDatabase.LoadAssetAtPath<T>(Root + "/" + rel);
            a.bossTripo = L<GameObject>("Art/Tripo/boss.fbx");
            a.minionTripo = L<GameObject>("Art/Tripo/minion.fbx");
            a.bossTripoController = L<RuntimeAnimatorController>("Anim/Boss_Tripo.controller");
            a.minionTripoController = L<RuntimeAnimatorController>("Anim/Minion_Tripo.controller");
            a.bossTripoAlbedo = L<Texture2D>("Art/Tripo/boss_albedo.png");
            a.bossTripoEmission = L<Texture2D>("Art/Tripo/boss_emission.png");
            a.minionTripoAlbedo = L<Texture2D>("Art/Tripo/minion_albedo.png");
            a.minionTripoEmission = L<Texture2D>("Art/Tripo/minion_emission.png");
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log($"TRIPO_ENEMIES_DONE boss={(a.bossTripo != null)} minion={(a.minionTripo != null)} bctrl={(a.bossTripoController != null)} mctrl={(a.minionTripoController != null)}");
        }

        [MenuItem("BlackTide/Tripo/Setup Weapons (长剑+巨斧)")]
        public static void SetupWeapons()
        {
            foreach (var n in new[] { "sword", "axe" })
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(Root + "/Art/Tripo/" + n + ".fbx");
                if (imp == null) { Debug.LogError("Missing weapon " + n); continue; }
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.isReadable = true;
                imp.SaveAndReimport();
                SetTexture(Root + "/Art/Tripo/" + n + "_albedo.png", true);
                SetTexture(Root + "/Art/Tripo/" + n + "_emission.png", true);
            }
            var a = AssetDatabase.LoadAssetAtPath<SliceAssets>(Root + "/Resources/SliceAssets.asset");
            T L<T>(string rel) where T : Object => AssetDatabase.LoadAssetAtPath<T>(Root + "/" + rel);
            a.swordTripo = L<GameObject>("Art/Tripo/sword.fbx");
            a.axeTripo = L<GameObject>("Art/Tripo/axe.fbx");
            a.swordTripoAlbedo = L<Texture2D>("Art/Tripo/sword_albedo.png");
            a.swordTripoEmission = L<Texture2D>("Art/Tripo/sword_emission.png");
            a.axeTripoAlbedo = L<Texture2D>("Art/Tripo/axe_albedo.png");
            a.axeTripoEmission = L<Texture2D>("Art/Tripo/axe_emission.png");
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log($"TRIPO_WEAPONS_DONE sword={(a.swordTripo != null)} axe={(a.axeTripo != null)}");
        }

        [MenuItem("BlackTide/Tripo/Setup Arena Ground (地面)")]
        public static void SetupArenaGround()
        {
            string d = Root + "/Art/Scene/";
            void Tex(string path, int max, bool srgb, bool normal, TextureWrapMode wrap)
            {
                var t = (TextureImporter)AssetImporter.GetAtPath(path);
                if (t == null) { Debug.LogError("Missing " + path); return; }
                t.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                t.maxTextureSize = max; t.sRGBTexture = srgb; t.wrapMode = wrap; t.anisoLevel = 8;
                t.textureCompression = TextureImporterCompression.CompressedHQ;
                t.SaveAndReimport();
            }
            Tex(d + "ground_albedo.jpg", 4096, true, false, TextureWrapMode.Clamp);
            Tex(d + "ground_normal.png", 2048, false, true, TextureWrapMode.Clamp);
            Tex(d + "ground_emission.png", 4096, true, false, TextureWrapMode.Clamp);
            Tex(d + "floor_tile.png", 2048, true, false, TextureWrapMode.Repeat);
            var a = AssetDatabase.LoadAssetAtPath<SliceAssets>(Root + "/Resources/SliceAssets.asset");
            a.groundBake = AssetDatabase.LoadAssetAtPath<Texture2D>(d + "ground_albedo.jpg");
            a.groundBakeNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(d + "ground_normal.png");
            a.groundBakeEmission = AssetDatabase.LoadAssetAtPath<Texture2D>(d + "ground_emission.png");
            a.floorTile = AssetDatabase.LoadAssetAtPath<Texture2D>(d + "floor_tile.png");
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log($"ARENA_GROUND_DONE bake={(a.groundBake != null)} n={(a.groundBakeNormal != null)} em={(a.groundBakeEmission != null)} tile={(a.floorTile != null)}");
        }

        // 场景道具：Art/Scene/<名字>.fbx + <名字>_albedo.png + <名字>_emission.png（自发光图可以没有）
        [MenuItem("BlackTide/Tripo/Setup Arena Props (场景道具)")]
        public static void SetupArenaProps()
        {
            string d = Root + "/Art/Scene/";
            PropAsset Load(string n)
            {
                var p = new PropAsset();
                var imp = (ModelImporter)AssetImporter.GetAtPath(d + n + ".fbx");
                if (imp == null) { Debug.LogWarning("Missing prop " + n + "（竞技场会用占位几何体）"); return p; }
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.addCollider = false;
                imp.SaveAndReimport();
                SetTexture(d + n + "_albedo.png", true);
                if (File.Exists(d + n + "_emission.png")) SetTexture(d + n + "_emission.png", true);
                p.model = AssetDatabase.LoadAssetAtPath<GameObject>(d + n + ".fbx");
                p.albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(d + n + "_albedo.png");
                p.emission = AssetDatabase.LoadAssetAtPath<Texture2D>(d + n + "_emission.png");
                return p;
            }
            var a = AssetDatabase.LoadAssetAtPath<SliceAssets>(Root + "/Resources/SliceAssets.asset");
            a.pillarBroken = Load("pillar_broken");
            a.pillarFallen = Load("pillar_fallen");
            a.brazier = Load("brazier");
            a.tideCrystal = Load("tide_crystal");
            a.wallRuin = Load("wall_ruin");
            a.statueHooded = Load("statue_hooded");
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            string S(PropAsset p) => p.model ? (p.emission ? "ok+em" : "ok") : "-";
            Debug.Log($"ARENA_PROPS_DONE pillar={S(a.pillarBroken)} fallen={S(a.pillarFallen)} brazier={S(a.brazier)} crystal={S(a.tideCrystal)} wall={S(a.wallRuin)} statue={S(a.statueHooded)}");
        }

        // 青岚、澄音：和绯烈一样按人形导入，直接用绯烈的动画控制器（同一套 KayKit 人形动画，状态名一致）
        [MenuItem("BlackTide/Tripo/Setup Allies (青岚+澄音+武器)")]
        public static void SetupAllies()
        {
            string d = Root + "/Art/Tripo/";
            foreach (var n in new[] { "qinglan", "chengyin" })
            {
                if (!File.Exists(d + n + ".fbx")) { Debug.LogWarning("Missing " + n + ".fbx（会用 KayKit 占位）"); continue; }
                SetupHumanoidModel(d + n + ".fbx", null);
                SetTexture(d + n + "_albedo.png", true);
            }
            foreach (var n in new[] { "pistol", "instrument" })
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(d + n + ".fbx");
                if (imp == null) continue;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.isReadable = true;
                imp.SaveAndReimport();
                SetTexture(d + n + "_albedo.png", true);
            }
            if (!AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Root + "/Anim/Feilie.controller")) SetupHero();
            var a = AssetDatabase.LoadAssetAtPath<SliceAssets>(Root + "/Resources/SliceAssets.asset");
            T L<T>(string rel) where T : Object => AssetDatabase.LoadAssetAtPath<T>(d + rel);
            a.qinglanTripo = L<GameObject>("qinglan.fbx");
            a.chengyinTripo = L<GameObject>("chengyin.fbx");
            a.qinglanAlbedo = L<Texture2D>("qinglan_albedo.png");
            a.chengyinAlbedo = L<Texture2D>("chengyin_albedo.png");
            a.pistolTripo = L<GameObject>("pistol.fbx");
            a.pistolAlbedo = L<Texture2D>("pistol_albedo.png");
            a.instrumentTripo = L<GameObject>("instrument.fbx");
            a.instrumentAlbedo = L<Texture2D>("instrument_albedo.png");
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log($"TRIPO_ALLIES_DONE qinglan={(a.qinglanTripo != null)} chengyin={(a.chengyinTripo != null)} pistol={(a.pistolTripo != null)} instrument={(a.instrumentTripo != null)}");
        }

        // A3 地图道具：Art/Map/ 下所有 <编号>_<名字>.fbx，不导材质、不导动画，登记进 SliceAssets.mapProps
        [MenuItem("BlackTide/Tripo/Setup Map Props (地图道具)")]
        public static void SetupMapProps()
        {
            string d = Root + "/Art/Map/";
            var a = AssetDatabase.LoadAssetAtPath<SliceAssets>(Root + "/Resources/SliceAssets.asset");
            a.mapProps.Clear();
            var names = new List<string>();
            foreach (var f in Directory.GetFiles(d, "*.fbx")) names.Add(Path.GetFileNameWithoutExtension(f));
            names.Sort();
            foreach (var n in names)
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(d + n + ".fbx");
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.addCollider = false;
                imp.isReadable = true;   // 刺鳐等要在运行时读网格算朝向
                imp.SaveAndReimport();
                SetTexture(d + n + "_albedo.png", true);
                if (File.Exists(d + n + "_emission.png")) SetTexture(d + n + "_emission.png", true);
                a.mapProps.Add(new PropAsset
                {
                    id = n,
                    model = AssetDatabase.LoadAssetAtPath<GameObject>(d + n + ".fbx"),
                    albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(d + n + "_albedo.png"),
                    emission = AssetDatabase.LoadAssetAtPath<Texture2D>(d + n + "_emission.png"),
                });
            }
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log($"MAP_PROPS_DONE count={a.mapProps.Count} ids={string.Join(",", names)}");
        }

        // 第四批（敌人，2026-10-09 核对完）：先把工程外 Incoming/batch4 里离线处理好的文件（改名、2K 贴图、自发光）搬进来，再设置导入
        public static readonly string[] Batch4Chars = { "shield_husk", "bloat_husk", "chanter_husk", "tideblade_duelist", "coral_javelineer", "tide_priest", "frost_witch", "thunder_colossus", "void_reaper", "tide_sovereign" };
        public static readonly string[] Batch4Weapons = { "coral_javelin", "frost_staff", "anchor_hammer", "void_sickle", "sovereign_trident" };

        [MenuItem("BlackTide/Tripo/Setup Batch 4 (敌人)")]
        public static void SetupBatch4()
        {
            string inc = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Incoming", "batch4"));
            string d = Root + "/Art/Tripo/Batch4/";
            Directory.CreateDirectory(d);
            int moved = 0;
            foreach (var sub in new[] { "Tripo", "Map" })
            {
                string from = Path.Combine(inc, sub);
                if (!Directory.Exists(from)) continue;
                string to = sub == "Tripo" ? d : Root + "/Art/Map/";
                foreach (var f in Directory.GetFiles(from)) { File.Copy(f, to + Path.GetFileName(f), true); moved++; }
            }
            AssetDatabase.Refresh();
            foreach (var n in Batch4Chars)
            {
                if (!File.Exists(d + n + ".fbx")) { Debug.LogWarning("Missing " + n + ".fbx"); continue; }
                SetupHumanoidModel(d + n + ".fbx", null);
            }
            foreach (var n in Batch4Weapons)
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(d + n + ".fbx");
                if (imp == null) { Debug.LogWarning("Missing " + n + ".fbx"); continue; }
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.isReadable = true;   // WeaponFit 要读网格算朝向
                imp.SaveAndReimport();
            }
            var a = AssetDatabase.LoadAssetAtPath<SliceAssets>(Root + "/Resources/SliceAssets.asset");
            a.batch4.Clear();
            foreach (var n in Batch4Chars.Concat(Batch4Weapons))
            {
                SetTexture(d + n + "_albedo.png", true);
                if (File.Exists(d + n + "_emission.png")) SetTexture(d + n + "_emission.png", true);
                a.batch4.Add(new PropAsset
                {
                    id = n,
                    model = AssetDatabase.LoadAssetAtPath<GameObject>(d + n + ".fbx"),
                    albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(d + n + "_albedo.png"),
                    emission = AssetDatabase.LoadAssetAtPath<Texture2D>(d + n + "_emission.png"),
                });
            }
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            SetupMapProps();   // 54 浮游水母、56 孵化囊、58 潮汐触手进地图道具表
            int ok = 0; foreach (var p in a.batch4) if (p.model) ok++;
            Debug.Log($"TRIPO_BATCH4_DONE moved={moved} models={ok}/{a.batch4.Count}");
        }

        // 第三批角色：C1 重甲潮兵、C2 信使（用骷髅战士 / 骷髅小兵的人形动画）、C3 商人、C4 探员（只用待机）
        [MenuItem("BlackTide/Tripo/Setup Batch 3 (潮兵+信使+商人+探员)")]
        public static void SetupBatch3()
        {
            string d = Root + "/Art/Tripo/";
            foreach (var n in new[] { "brute", "messenger", "merchant", "scout" })
            {
                if (!File.Exists(d + n + ".fbx")) { Debug.LogWarning("Missing " + n + ".fbx"); continue; }
                SetupHumanoidModel(d + n + ".fbx", null);
                SetTexture(d + n + "_albedo.png", true);
                if (File.Exists(d + n + "_emission.png")) SetTexture(d + n + "_emission.png", true);
            }
            var imp = (ModelImporter)AssetImporter.GetAtPath(d + "glaive.fbx");
            if (imp != null)
            {
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.isReadable = true;
                imp.SaveAndReimport();
                SetTexture(d + "glaive_albedo.png", true);
                if (File.Exists(d + "glaive_emission.png")) SetTexture(d + "glaive_emission.png", true);
            }
            var a = AssetDatabase.LoadAssetAtPath<SliceAssets>(Root + "/Resources/SliceAssets.asset");
            T L<T>(string rel) where T : Object => AssetDatabase.LoadAssetAtPath<T>(d + rel);
            a.bruteTripo = L<GameObject>("brute.fbx"); a.bruteAlbedo = L<Texture2D>("brute_albedo.png"); a.bruteEmission = L<Texture2D>("brute_emission.png");
            a.messengerTripo = L<GameObject>("messenger.fbx"); a.messengerAlbedo = L<Texture2D>("messenger_albedo.png"); a.messengerEmission = L<Texture2D>("messenger_emission.png");
            a.merchantTripo = L<GameObject>("merchant.fbx"); a.merchantAlbedo = L<Texture2D>("merchant_albedo.png");
            a.scoutTripo = L<GameObject>("scout.fbx"); a.scoutAlbedo = L<Texture2D>("scout_albedo.png");
            a.glaiveTripo = L<GameObject>("glaive.fbx"); a.glaiveAlbedo = L<Texture2D>("glaive_albedo.png"); a.glaiveEmission = L<Texture2D>("glaive_emission.png");
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log($"TRIPO_BATCH3_DONE brute={(a.bruteTripo != null)} messenger={(a.messengerTripo != null)} merchant={(a.merchantTripo != null)} scout={(a.scoutTripo != null)} glaive={(a.glaiveTripo != null)}");
        }

        [MenuItem("BlackTide/Tripo/Rebuild Hero Controller")]
        public static void RebuildHeroController()
        {
            BuildController(Root + "/Anim/Feilie.controller", KnightHumanoid, "2H_Melee_Idle");
            var a = AssetDatabase.LoadAssetAtPath<SliceAssets>(Root + "/Resources/SliceAssets.asset");
            a.heroTripoController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Root + "/Anim/Feilie.controller");
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
        }

        static void BuildKnightHumanoid() => BuildHumanoidAnims(KnightSrc, KnightHumanoid);

        // 复制一份 KayKit 模型，按人形导入，它的动画就能给任何人形角色用
        static void BuildHumanoidAnims(string src, string dst)
        {
            if (!AssetDatabase.IsValidFolder(Root + "/Art/Anim")) AssetDatabase.CreateFolder(Root + "/Art", "Anim");
            if (!File.Exists(dst)) AssetDatabase.CopyAsset(src, dst);
            AssetDatabase.ImportAsset(dst);
            SetupHumanoidModel(dst, KayKitMap);

            var imp = (ModelImporter)AssetImporter.GetAtPath(dst);
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
                c.loopTime = LoopClips.Contains(c.name);
                // 原地动画：根节点的旋转、高度、水平位移都烘进姿势，移动交给角色控制器
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
        }

        // 人形导入：先自动映射一遍拿到骨架信息，再按需要覆盖映射、摆 T 字姿势，最后重新导入
        static void SetupHumanoidModel(string path, Dictionary<string, string> map)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp == null) { Debug.LogError("Missing " + path); return; }
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();

            var hd = imp.humanDescription;
            if (map != null)
            {
                hd.human = map.Select(kv => new HumanBone { humanName = kv.Key, boneName = kv.Value, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
            }
            var human = hd.human.ToDictionary(h => h.humanName, h => h.boneName);

            // 在一个临时实例上把手臂抬平、腿伸直，再把骨骼姿势写回参考骨架
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = (GameObject)Object.Instantiate(asset);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var all = go.GetComponentsInChildren<Transform>(true);
            Transform F(string h) => human.TryGetValue(h, out var b) ? all.FirstOrDefault(t => t.name == b) : null;
            var hips = F("Hips");
            void Align(string from, string to, Vector3 dir)
            {
                var a = F(from); var b = F(to);
                if (!a || !b) return;
                var cur = (b.position - a.position).normalized;
                a.rotation = Quaternion.FromToRotation(cur, dir) * a.rotation;
            }
            if (hips)
            {
                float lx = Mathf.Sign((F("LeftUpperArm") ? F("LeftUpperArm").position.x : -1f) - hips.position.x);
                Align("LeftUpperArm", "LeftLowerArm", new Vector3(lx, 0, 0));
                Align("LeftLowerArm", "LeftHand", new Vector3(lx, 0, 0));
                Align("RightUpperArm", "RightLowerArm", new Vector3(-lx, 0, 0));
                Align("RightLowerArm", "RightHand", new Vector3(-lx, 0, 0));
                Align("LeftUpperLeg", "LeftLowerLeg", Vector3.down);
                Align("LeftLowerLeg", "LeftFoot", Vector3.down);
                Align("RightUpperLeg", "RightLowerLeg", Vector3.down);
                Align("RightLowerLeg", "RightFoot", Vector3.down);
            }
            var byName = new Dictionary<string, Transform>();
            foreach (var t in all) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            var sk = hd.skeleton;
            for (int i = 0; i < sk.Length; i++)
            {
                if (i == 0) continue; // 根节点保持不变
                if (byName.TryGetValue(sk[i].name, out var t))
                {
                    sk[i].position = t.localPosition;
                    sk[i].rotation = t.localRotation;
                    sk[i].scale = t.localScale;
                }
            }
            hd.skeleton = sk;
            Object.DestroyImmediate(go);

            imp.humanDescription = hd;
            imp.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            Debug.Log($"Humanoid {Path.GetFileName(path)}: avatar={(avatar != null)} valid={(avatar != null && avatar.isValid)} human={(avatar != null && avatar.isHuman)} bones={hd.human.Length}");
        }

        static void BuildController(string cpath, string animSrc, string defName)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(cpath)) AssetDatabase.DeleteAsset(cpath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(cpath);
            var sm = ctrl.layers[0].stateMachine;
            var clips = AssetDatabase.LoadAllAssetsAtPath(animSrc).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            AnimatorState def = null;
            foreach (var c in clips)
            {
                var st = sm.AddState(c.name);
                st.motion = c;
                st.writeDefaultValues = true;
                st.iKOnFeet = false;
                if (c.name == defName) def = st;
            }
            if (def != null) sm.defaultState = def;
            var layers = ctrl.layers;
            layers[0].iKPass = true; // 左手贴剑柄要用 IK
            ctrl.layers = layers;
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            Debug.Log($"Controller {Path.GetFileName(cpath)}: {clips.Length} states, ik={ctrl.layers[0].iKPass}");
        }

        static void SetTexture(string path, bool srgb)
        {
            var t = (TextureImporter)AssetImporter.GetAtPath(path);
            if (t == null) { Debug.LogWarning("Missing texture " + path); return; }
            t.maxTextureSize = 2048;
            t.sRGBTexture = srgb;
            t.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            t.anisoLevel = 4;
            t.SaveAndReimport();
        }
    }
}
