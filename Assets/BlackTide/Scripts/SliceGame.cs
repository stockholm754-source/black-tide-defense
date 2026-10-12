using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlackTide
{
    // 启动：搭场景、生成角色、接好镜头和界面；F2 重开（R 是声骸技能）
    public class SliceGame : MonoBehaviour
    {
        public static SliceGame I;
        public bool runMode;            // true：开放地图一局（BlackTide_Run 场景）；false：Boss 竞技场切片
        public static bool RunMode => I && I.runMode;
        HUD hud;
        CameraRig rig;
        float startTime;
        float endTime = -1f;

        public string TimerText => runMode && RunDirector.I ? RunDirector.I.StatusLine : endTime > 0f ? $"用时 {endTime - startTime:0.0} 秒" : $"战斗时间 {Time.time - startTime:0.0} 秒";

        void Awake()
        {
            I = this;
            Time.timeScale = 1f;
            Combat.enemies.Clear();
            Combat.threats.Clear();
            Combat.heroes.Clear();
            WindWall.All.Clear();
            Combat.bosses.Clear();
            Combat.clampOverride = null;
            Combat.arenaRadius = 18.5f;
            Nav.Clear();
            Combat.boss = null;
            Combat.player = null;
            Coop.Reset();
            Feel.BaseScale = 1f;
            Application.targetFrameRate = 120;
            Arsenal.Clear();   // DS-019：弹幕军火库跨局清理
            new GameObject("Feel").AddComponent<Feel>();
            FX.Init();
            rig = Camera.main ? Camera.main.GetComponent<CameraRig>() : null;
            if (runMode)
            {
                // 开放地图：出生点在西南角，三人朝地图中心站
                MapBuilder.Build();
                Vector3 sp = new Vector3(MapLayout.Spawn.x, 0f, MapLayout.Spawn.y);
                Vector3 fwd = (-sp).normalized, right = Vector3.Cross(Vector3.up, fwd);
                SpawnPlayer(sp + fwd * 2f);
                SpawnAlly(HeroId.Qinglan, sp - right * 3f);
                SpawnAlly(HeroId.Chengyin, sp + right * 3f);
                foreach (var h in Combat.heroes) h.transform.rotation = Quaternion.LookRotation(fwd);
                gameObject.AddComponent<RunDirector>();
            }
            else
            {
                ArenaBuilder.Build();
                SpawnPlayer(new Vector3(0f, 0f, -7f));
                SpawnAlly(HeroId.Qinglan, new Vector3(-3.5f, 0f, -9f));
                SpawnAlly(HeroId.Chengyin, new Vector3(3.5f, 0f, -9.5f));
                SpawnBoss(new Vector3(0f, 0f, 5f));
            }
            hud = new GameObject("HUD").AddComponent<HUD>();
            hud.Build();
            if (rig)
            {
                // 斜俯视：视野纵深约 21 米，能同时框住玩家和 Boss
                rig.distance = 22.5f; rig.pitch = 54f; rig.fov = 34f;
                // 开放地图：动态取景（跑图 17 米，交战按敌人范围推近到 12.5–19 米），视角放宽到 40°
                if (runMode) { rig.dynamicZoom = true; rig.pitch = 52f; rig.fov = 40f; }
                Camera.main.fieldOfView = rig.fov;
                Camera.main.farClipPlane = runMode ? 260f : 200f;
                rig.target = Combat.player.transform;
                rig.focus2 = Combat.boss ? Combat.boss.transform : null;
                rig.Snap();
            }
            startTime = Time.time;
        }

        void Update()
        {
            Coop.Tick();
            if (!ConsoleUI.IsOpen)
            {
                if (Input.GetKeyDown(KeyCode.F1)) { HeroBase.Autopilot = !HeroBase.Autopilot; Combat.Toast(HeroBase.Autopilot ? "自动代打 开（F1 关闭）" : "自动代打 关", Color.white); }
                if (!GameInput.CombatBlocked && GameInput.Down(GameAction.SwitchHero)) SwitchControl();
                if (Input.GetKeyDown(KeyCode.F3)) CycleErosion();
                // CL-005：特效风格从 F6 挪到 F8。F6 在开放地图里是「切换路线 AI」（RunDirector:189），
                // 两个都绑 F6 时按一次会同时换路线和换画风——原来就存在的撞键，做三态时暴露出来。
                if (Input.GetKeyDown(KeyCode.F8)) { FX.NextStyle(); Combat.Toast(FX.StyleHint, new Color(0.85f, 0.88f, 0.95f)); }
                if (Input.GetKeyDown(KeyCode.F9)) Recorder.Begin("rec_" + System.DateTime.Now.ToString("HHmmss"), 8f);
                if (GameInput.Down(GameAction.Restart)) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0 ? SceneManager.GetActiveScene().buildIndex : 0);
            }
        }

        public void OnBossDefeated()
        {
            endTime = Time.time;
            string rep = Coop.Report(endTime - startTime);
            hud.ShowEnd($"击败 怒涛 · 潮汐骑\n<size=28><color=#e8dccf>用时 {endTime - startTime:0.0} 秒    按 {GameInput.Hint("F2","LT+Menu")} 再来一次\n{rep}</color></size>");
            LogReport("WIN");
        }

        public void OnTeamWipe()
        {
            if (runMode && RunDirector.I) { RunDirector.I.OnWipe(); return; }
            if (endTime > 0f) return;
            endTime = Time.time;
            hud.ShowEnd($"<color=#ff7a6a>全队覆灭</color>\n<size=28><color=#e8dccf>坚持 {endTime - startTime:0.0} 秒    原稿规则：团灭会让黑潮增速 +5%    按 {GameInput.Hint("F2","LT+Menu")} 再来一次\n{Coop.Report(endTime - startTime)}</color></size>");
            LogReport("WIPE");
        }

        void LogReport(string result)
        {
            var sb = new System.Text.StringBuilder($"COOP_REPORT result={result} t={endTime - startTime:0.0} erosion={Coop.erosion:0} bossHp={(Combat.boss ? Combat.boss.dmg.hp : 0f):0}");
            float total = 0f;
            foreach (var h in Combat.heroes) total += h.dmgDone;
            foreach (var h in Combat.heroes)
                sb.Append($" | {h.HeroName} dmg={h.dmgDone:0}({(total > 0f ? h.dmgDone / total * 100f : 0f):0}%) heal={h.healDone:0} pd={h.perfectDodges} relay={h.relaysDone} rescue={h.rescues} downs={h.downCount}");
            sb.Append($" | team={Coop.teamSkills} breaks={Coop.breaks} chantCut={Coop.chantsCut} chantHit={Coop.chantsHit} downs={Coop.downs}");
            Debug.Log(sb.ToString());
        }

        // Tab：切换操控的角色，另外两个交给 AI
        void SwitchControl()
        {
            if (Combat.heroes.Count == 0) return;
            int i = Combat.heroes.IndexOf(Combat.player);
            Combat.player = Combat.heroes[(i + 1) % Combat.heroes.Count];
            FX.Mark(null);
            Combat.player.RefreshCombatTarget();
            if (rig) { rig.target = Combat.player.transform; }
            Combat.Toast($"操控：{Combat.player.HeroName}（{Combat.player.Role}）", Combat.player.Color);
        }

        // F3：切换侵蚀预设（40% 无超频 → 70% → 100%），用来看超频和净频
        void CycleErosion()
        {
            Coop.erosion = Coop.erosion < 45f ? 70f : Coop.erosion < 99f ? 100f : 40f;
            Combat.Toast($"侵蚀 {Coop.erosion:0}%{(Coop.OcActive ? " · 超频开启" : " · 超频未激活")}", new Color(0.4f, 0.95f, 0.9f));
        }

        // ---------------- 角色生成 ----------------
        static GameObject SpawnModel(Transform parent, GameObject prefab, RuntimeAnimatorController ctrl)
        {
            var model = Instantiate(prefab, parent);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var animator = model.GetComponent<Animator>();
            if (!animator) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = ctrl;
            animator.applyRootMotion = false;
            return model;
        }

        static Material CharMat(Texture2D tex, Color tint, Color emission)
        {
            var m = ArenaBuilder.Lit(tex, tint, 0.22f);
            m.SetColor("_EmissionColor", emission);
            return m;
        }

        static void ApplyMat(GameObject model, Material body, Material eyes)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = eyes != null && r.name.Contains("Eyes") ? eyes : body;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        // 按网格包围盒找出武器的"根"和"尖"，给刀光用
        static void WeaponEnds(Transform weapon, float baseFrac, out Transform b, out Transform tip)
        {
            Mesh mesh = null;
            Transform mt = weapon;
            var mf = weapon.GetComponentInChildren<MeshFilter>();
            if (mf) { mesh = mf.sharedMesh; mt = mf.transform; }
            var bounds = mesh ? mesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            Vector3 e = bounds.extents;
            Vector3 axis = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
            float lo = Vector3.Dot(bounds.min, axis), hi = Vector3.Dot(bounds.max, axis);
            float tipV = Mathf.Abs(hi) >= Mathf.Abs(lo) ? hi : lo;
            float baseV = Mathf.Abs(hi) >= Mathf.Abs(lo) ? lo : hi;
            Vector3 ctr = bounds.center - axis * Vector3.Dot(bounds.center, axis);
            b = new GameObject("TrailBase").transform; b.SetParent(mt, false);
            b.localPosition = ctr + axis * Mathf.Lerp(baseV, tipV, baseFrac);
            tip = new GameObject("TrailTip").transform; tip.SetParent(mt, false);
            tip.localPosition = ctr + axis * tipV;
        }

        static Transform AttachWeapon(GameObject model, GameObject weaponPrefab)
        {
            var slot = Find(model.transform, "handslot.r");
            if (!slot || !weaponPrefab) return null;
            var w = Instantiate(weaponPrefab);
            Quaternion rot = w.transform.localRotation;
            Vector3 sc = w.transform.localScale;
            w.transform.SetParent(slot, false);
            w.transform.localPosition = Vector3.zero;
            w.transform.localRotation = rot;
            Vector3 ls = slot.lossyScale;
            w.transform.localScale = new Vector3(sc.x / ls.x, sc.y / ls.y, sc.z / ls.z);
            return w.transform;
        }

        // 人形骨骼的"头顶高度"：优先用 HeadTop_End，没有就用渲染包围盒
        static float ModelHeight(GameObject go, Animator an)
        {
            var top = Find(go.transform, "mixamorig:HeadTop_End");
            if (top) return top.position.y - go.transform.position.y;
            float max = go.transform.position.y, min = max;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.name.Contains("Sword") || r.name.Contains("Shield") || r.name.Contains("Axe") || r.name.Contains("Blade")) continue;
                max = Mathf.Max(max, r.bounds.max.y); min = Mathf.Min(min, r.bounds.min.y);
            }
            return max - min;
        }

        public static float HeroHeight = 2.05f;

        static Material TripoMat(Texture2D alb, Texture2D em, Color tint, Color emCol, float smooth)
        {
            var m = ArenaBuilder.Lit(alb, tint, smooth);
            if (em)
            {
                m.SetTexture("_EmissionMap", em);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emCol);
            }
            return m;
        }

        // 隐形参考模型（原版 KayKit + 武器）→ 复制一把武器给 Tripo 模型，并挂上武器跟随和左手 IK
        static WeaponFollow AttachFollow(Transform root, GameObject model, Animator anim, GameObject refModel, Transform refWeapon, float s, Material weaponMat, out Transform weapon)
        {
            weapon = Instantiate(refWeapon.gameObject, root).transform;
            weapon.name = "TripoWeapon";
            weapon.localScale = refWeapon.lossyScale * s;
            foreach (var r in weapon.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
                r.sharedMaterial = weaponMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            var f = root.gameObject.AddComponent<WeaponFollow>();
            f.heroRoot = model.transform;
            f.heroHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            f.refRoot = refModel.transform;
            f.refHand = Find(refModel.transform, "wrist.r");
            f.refLeft = Find(refModel.transform, "wrist.l");
            f.refSword = refWeapon;
            f.sword = weapon;
            f.scale = s;
            model.AddComponent<LeftHandIK>().follow = f;
            return f;
        }

        static void HideRenderers(GameObject go) { foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false; }

        static void SyncMirror(GameObject root, Animator refAnim)
        {
            var ca = root.GetComponent<CharacterAnim>();
            if (!ca || !refAnim) return;
            ca.mirror = refAnim;
            refAnim.speed = ca.animator.speed;
            if (!string.IsNullOrEmpty(ca.current)) refAnim.Play(ca.current, 0, 0f);
        }

        static void BossAura(Transform root)
        {
            var A = SliceAssets.I;
            var aura = new GameObject("Aura");
            aura.transform.SetParent(root, false);
            var ps = aura.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.5f); main.startColor = new Color(0.25f, 0.95f, 0.9f, 0.7f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -0.15f;
            var em = ps.emission; em.rateOverTime = 26f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1.4f; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0, 1) }); col.color = g;
            aura.GetComponent<ParticleSystemRenderer>().sharedMaterial = FX.NewMat(A.softDot, true, Color.white, 2f);
        }

        void SpawnBossTripo(Vector3 pos)
        {
            var A = SliceAssets.I;
            SpawnBossTripo(pos, "潮汐骑", A.bossTripo, A.bossTripoAlbedo, A.bossTripoEmission, -1f, A.axeTripo, A.axeTripoAlbedo, A.axeTripoEmission, 1.9f);
        }

        // height < 0：原版骷髅战士身高的 0.95 倍（约 5 米）；weaponMul：Tripo 武器相对原版斧头的长度倍数
        void SpawnBossTripo(Vector3 pos, string name, GameObject prefab, Texture2D albedo, Texture2D emission, float height,
            GameObject weaponPrefab, Texture2D weaponAlbedo, Texture2D weaponEmission, float weaponMul)
        {
            var A = SliceAssets.I;
            var root = new GameObject(name);
            root.transform.position = pos;
            var model = SpawnModel(root.transform, prefab, A.bossTripoController);
            var anim = model.GetComponent<Animator>();
            ApplyMat(model, TripoMat(albedo, emission, new Color(0.92f, 0.9f, 0.9f), new Color(0.25f, 1f, 0.95f) * 3.5f, 0.32f), null);

            // 原版骷髅战士（隐形）+ 斧头，决定体型和斧头轨迹
            var refModel = SpawnModel(root.transform, A.bossModel, A.bossController);
            refModel.name = "WeaponRef";
            refModel.transform.localScale = Vector3.one * 2.1f;
            var refAnim = refModel.GetComponent<Animator>();
            refAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var refAxe = AttachWeapon(refModel, A.axeModel);
            float refH = ModelHeight(refModel, refAnim);
            HideRenderers(refModel);
            float bossH = height > 0f ? height : refH * 0.95f;
            float h = ModelHeight(model, anim);
            if (h > 0.01f) model.transform.localScale = Vector3.one * (bossH / h);

            Transform b = null, tip = null;
            if (refAxe && anim.isHuman)
            {
                var axeMat = CharMat(A.skeletonTexture, new Color(0.35f, 0.4f, 0.42f), new Color(0.05f, 0.35f, 0.33f));
                AttachFollow(root.transform, model, anim, refModel, refAxe, bossH / refH, axeMat, out var axe);
                WeaponEnds(axe, 0.45f, out b, out tip);
                if (weaponPrefab)
                {
                    float axeMul = weaponMul; // Boss 体型大，斧头要够大才压得住画面（潮汐骑 1.9）
                    WeaponFit.Fit(axe, weaponPrefab, TripoMat(weaponAlbedo, weaponEmission, new Color(0.85f, 0.9f, 0.92f), new Color(0.25f, 1f, 0.95f) * 4f, 0.35f), true, axeMul);
                    // 刀光端点按武器加长比例外推（原版武器的枢轴就是握把）
                    if (b) b.localPosition *= axeMul;
                    if (tip) tip.localPosition *= axeMul;
                }
            }
            root.transform.rotation = Quaternion.LookRotation(Vector3.back);
            var bc = root.AddComponent<BossController>();
            bc.Setup(model, b, tip);
            SyncMirror(root, refAnim);
            bc.dmg.RefreshRenderers();
            BossAura(root.transform);
        }

        static float minionTripoH = -1f;
        static Material minionTripoMat;

        static Material bruteMat;

        // DS-018：黑潮王召唤的残像（标记后死亡掉治疗微粒、不占节点生成额度）
        public static bool spawningSummoned;
        // DS-018b：黑潮追猎者（侵蚀驱动的追击怪，计入存活上限）
        public static bool spawningHunter;
        // 割草方向重做：黑潮涌浪的兵（持续怪潮，经验/贝币打折、不掉声骸武器）
        public static bool spawningTide;

        public bool SpawnMinionTripo(Vector3 pos, float hpMult, float dmgMult, float size, HeroBase owner)
        {
            var A = SliceAssets.I;
            if (!A.minionTripo || !A.minionTripoController) return false;
            if (size >= 1.3f && A.bruteTripo)
            {
                // 巨浪 = C1 重甲潮兵：用骷髅小兵的人形动画（状态名一样），身高约 3 米；残像的体型系数换算成身高
                if (!bruteMat) bruteMat = TripoMat(A.bruteAlbedo, A.bruteEmission, Color.white, new Color(0.25f, 1f, 0.95f) * 3f, 0.4f);
                var broot = new GameObject("巨浪·重甲潮兵");
                broot.transform.position = pos;
                broot.transform.rotation = Quaternion.LookRotation(Combat.player ? (Combat.player.transform.position - pos).normalized : Vector3.back);
                var bmodel = SpawnModel(broot.transform, A.bruteTripo, A.minionTripoController);
                ApplyMat(bmodel, bruteMat, null);
                float bh = ModelHeight(bmodel, bmodel.GetComponent<Animator>());
                if (bh > 0.01f) bmodel.transform.localScale = Vector3.one * (3.0f * size / 1.5f / bh);
                var bmc = broot.AddComponent<MinionController>();
                bmc.Setup(bmodel, hpMult, dmgMult, size, owner);
                if (spawningSummoned) bmc.summoned = true;
                if (spawningHunter) bmc.hunter = true;
                if (spawningTide) bmc.tide = true;
                bmc.dmg.RefreshRenderers();
                return true;
            }
            if (minionTripoH < 0f)
            {
                var tmp = Instantiate(A.minionModel);
                tmp.transform.localScale = Vector3.one * 0.9f;
                minionTripoH = ModelHeight(tmp, null) * 0.95f;
                tmp.SetActive(false); Destroy(tmp);
            }
            if (!minionTripoMat) minionTripoMat = TripoMat(A.minionTripoAlbedo, A.minionTripoEmission, Color.white, new Color(0.25f, 1f, 0.95f) * 3f, 0.45f);
            var root = new GameObject("残像");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.LookRotation(Combat.player ? (Combat.player.transform.position - pos).normalized : Vector3.back);
            var model = SpawnModel(root.transform, A.minionTripo, A.minionTripoController);
            ApplyMat(model, minionTripoMat, null);
            float h = ModelHeight(model, model.GetComponent<Animator>());
            if (h > 0.01f) model.transform.localScale = Vector3.one * (minionTripoH / h * size);
            var mc = root.AddComponent<MinionController>();
            mc.Setup(model, hpMult, dmgMult, size, owner);
            if (spawningSummoned) mc.summoned = true;
            if (spawningHunter) mc.hunter = true;
            if (spawningTide) mc.tide = true;
            mc.dmg.RefreshRenderers();
            return true;
        }

        void SpawnPlayerTripo(Vector3 pos)
        {
            var A = SliceAssets.I;
            var root = new GameObject("绯烈");
            root.transform.position = pos;
            var model = SpawnModel(root.transform, A.heroTripo, A.heroTripoController);
            var heroAnim = model.GetComponent<Animator>();

            var body = ArenaBuilder.Lit(A.heroTripoAlbedo, Color.white, 0.28f);
            if (A.heroTripoEmission)
            {
                body.SetTexture("_EmissionMap", A.heroTripoEmission);
                body.EnableKeyword("_EMISSION");
                body.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.2f) * 2.4f);
            }
            ApplyMat(model, body, null);

            // 统一身高
            float h = ModelHeight(model, heroAnim);
            if (h > 0.01f) model.transform.localScale = Vector3.one * (HeroHeight / h);

            // 隐形的原版骑士：和角色同步播放动作，剑的朝向和左手位置都照抄它
            Renderer swordR = null; Transform b = null, tip = null;
            var refModel = SpawnModel(root.transform, A.heroModel, A.heroController);
            refModel.name = "WeaponRef";
            refModel.transform.localScale = Vector3.one * 0.85f;
            var refAnim = refModel.GetComponent<Animator>();
            refAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            float refH = ModelHeight(refModel, refAnim);
            foreach (var r in refModel.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var refSword = Find(refModel.transform, "2H_Sword");
            WeaponFollow follow = null;
            if (refSword && heroAnim && heroAnim.isHuman)
            {
                float s = refH > 0.01f ? HeroHeight / refH : 1f;
                var sword = Instantiate(refSword.gameObject, root.transform).transform;
                sword.name = "HeroSword";
                sword.localScale = refSword.lossyScale * s;
                swordR = sword.GetComponent<Renderer>();
                swordR.enabled = true;
                swordR.sharedMaterial = CharMat(A.heroTexture, Color.white, new Color(1f, 0.38f, 0.1f) * 0.3f);
                swordR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                WeaponEnds(sword, 0.3f, out b, out tip);
                if (A.swordTripo)
                {
                    var r = WeaponFit.Fit(sword, A.swordTripo, TripoMat(A.swordTripoAlbedo, A.swordTripoEmission, Color.white, new Color(1f, 0.45f, 0.15f) * 3f, 0.4f), false, 1.05f);
                    if (r) swordR = r;
                }

                follow = root.AddComponent<WeaponFollow>();
                follow.heroRoot = model.transform;
                follow.heroHand = heroAnim.GetBoneTransform(HumanBodyBones.RightHand);
                follow.refRoot = refModel.transform;
                follow.refHand = Find(refModel.transform, "wrist.r");
                follow.refLeft = Find(refModel.transform, "wrist.l");
                follow.refSword = refSword;
                follow.sword = sword;
                follow.scale = s;
                model.AddComponent<LeftHandIK>().follow = follow;
            }

            var pc = root.AddComponent<FeilieController>();
            pc.rig = rig;
            pc.Setup(model, b, tip, swordR);
            Combat.player = pc;
            HeroMarker(pc);
            var ca = root.GetComponent<CharacterAnim>();
            if (ca) ca.mirror = refAnim;
            pc.dmg.RefreshRenderers();
            root.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        }

        void SpawnPlayer(Vector3 pos)
        {
            var A = SliceAssets.I;
            if (A.heroTripo && A.heroTripoController && A.knightHumanoid) { SpawnPlayerTripo(pos); return; }
            var root = new GameObject("绯烈");
            root.transform.position = pos;
            var model = SpawnModel(root.transform, A.heroModel, A.heroController);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                if (r.name.Contains("Shield") || r.name.StartsWith("1H_")) r.gameObject.SetActive(false);
            var body = CharMat(A.heroTexture, new Color(1f, 0.95f, 0.92f), Color.black);
            ApplyMat(model, body, null);
            var swordT = Find(model.transform, "2H_Sword");
            Renderer swordR = null;
            Transform b = null, tip = null;
            if (swordT)
            {
                swordR = swordT.GetComponent<Renderer>();
                var swordMat = CharMat(A.heroTexture, Color.white, new Color(1f, 0.38f, 0.1f) * 0.3f);
                swordR.sharedMaterial = swordMat;
                WeaponEnds(swordT, 0.3f, out b, out tip);
            }
            model.transform.localScale = Vector3.one * 0.85f;
            var pc = root.AddComponent<FeilieController>();
            pc.rig = rig;
            pc.Setup(model, b, tip, swordR);
            Combat.player = pc;
            HeroMarker(pc);
            pc.dmg.RefreshRenderers();
            root.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        }

        // 黑潮信使：C2 模型，约 3.5 米，拿 29 长戟（约 3 米）。没有 C2 时退回潮汐骑
        public void SpawnMessenger(Vector3 pos)
        {
            var A = SliceAssets.I;
            if (A.messengerTripo && A.bossTripoController)
            {
                SpawnBossTripo(pos, "黑潮信使", A.messengerTripo, A.messengerAlbedo, A.messengerEmission, 3.5f,
                    A.glaiveTripo ? A.glaiveTripo : A.axeTripo, A.glaiveTripo ? A.glaiveAlbedo : A.axeTripoAlbedo, A.glaiveTripo ? A.glaiveEmission : A.axeTripoEmission, 2.8f);
                return;
            }
            SpawnBoss(pos);
        }

        // 第四批怒涛：C11 霜潮女巫（60 法杖）、C12 雷锚巨兵（61 战锤）、C13 湮灭刺客（62 镰刀）。没导入第四批时退回潮汐骑的模型（招式照常）
        public void SpawnWildVariant(Vector3 pos, string variant, string name)
        {
            var A = SliceAssets.I;
            string id = variant == "frost" ? "frost_witch" : variant == "thunder" ? "thunder_colossus" : "void_reaper";
            string wid = variant == "frost" ? "frost_staff" : variant == "thunder" ? "anchor_hammer" : "void_sickle";
            float h = variant == "frost" ? 4.2f : variant == "thunder" ? 5.2f : 4.4f;
            float mul = variant == "thunder" ? 2.2f : 1.9f;
            var m = A.B4(id); var w = A.B4(wid);
            if (m != null && m.model && A.bossTripoController)
                SpawnBossTripo(pos, name, m.model, m.albedo, m.emission, h,
                    w != null && w.model ? w.model : A.axeTripo, w != null && w.model ? w.albedo : A.axeTripoAlbedo, w != null && w.model ? w.emission : A.axeTripoEmission, mul);
            else SpawnBoss(pos);
            var b = BossController.Last;
            if (b) b.MakeVariant(variant, name);
        }

        // 黑潮王（M5）：C14 模型约 6 米，拿 63 王之三叉戟；心脏位置一盏青色点光源，三阶段变亮。没导入第四批时退回潮汐骑
        public void SpawnSovereign(Vector3 pos)
        {
            var A = SliceAssets.I;
            var m = A.B4("tide_sovereign"); var w = A.B4("sovereign_trident");
            if (m != null && m.model && A.bossTripoController)
                SpawnBossTripo(pos, "黑潮王", m.model, m.albedo, m.emission, 6f,
                    w != null && w.model ? w.model : A.axeTripo, w != null && w.model ? w.albedo : A.axeTripoAlbedo, w != null && w.model ? w.emission : A.axeTripoEmission, 2.6f);
            else SpawnBoss(pos);
            var b = BossController.Last;
            if (!b) return;
            var lg = new GameObject("HeartLight").AddComponent<Light>();
            lg.transform.SetParent(b.transform, false);
            lg.transform.localPosition = new Vector3(0f, 3.9f, 0.6f);
            lg.type = LightType.Point; lg.color = new Color(0.3f, 1f, 0.92f); lg.intensity = 3f; lg.range = 7f; lg.shadows = LightShadows.None;
            b.MakeSovereign(lg);
        }

        // NPC（商人、探员）：人形模型 + 待机动作，没有战斗组件
        public GameObject SpawnNpc(string name, GameObject prefab, Texture2D albedo, Vector3 pos, Vector3 face, float height)
        {
            var A = SliceAssets.I;
            if (!prefab || !A.heroTripoController) return null;
            var root = new GameObject(name);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.back);
            var model = SpawnModel(root.transform, prefab, A.heroTripoController);
            ApplyMat(model, ArenaBuilder.Lit(albedo, Color.white, 0.25f), null);
            float h = ModelHeight(model, model.GetComponent<Animator>());
            if (h > 0.01f) model.transform.localScale = Vector3.one * (height / h);
            var an = model.GetComponent<Animator>();
            an.CrossFadeInFixedTime("Idle", 0f, 0, Random.value);
            an.speed = Random.Range(0.85f, 1.05f);
            return root;
        }

        public void SpawnBoss(Vector3 pos)
        {
            var A = SliceAssets.I;
            if (A.bossTripo && A.bossTripoController) { SpawnBossTripo(pos); return; }
            var root = new GameObject("潮汐骑");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.LookRotation(Vector3.back);
            var model = SpawnModel(root.transform, A.bossModel, A.bossController);
            var axe = AttachWeapon(model, A.axeModel);
            var body = CharMat(A.skeletonTexture, new Color(0.62f, 0.56f, 0.85f), new Color(0.08f, 0.02f, 0.16f));
            var eyes = CharMat(A.skeletonTexture, new Color(0.6f, 0.3f, 1f), new Color(1.6f, 0.7f, 4f));
            ApplyMat(model, body, eyes);
            Transform b = null, tip = null;
            if (axe) WeaponEnds(axe, 0.45f, out b, out tip);
            model.transform.localScale = Vector3.one * 2.1f;
            var bc = root.AddComponent<BossController>();
            bc.Setup(model, b, tip);
            bc.dmg.RefreshRenderers();
            // 黑潮缠身：脚下持续冒出的紫色微粒
            var aura = new GameObject("Aura");
            aura.transform.SetParent(root.transform, false);
            var ps = aura.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.5f); main.startColor = new Color(0.6f, 0.3f, 1f, 0.7f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -0.15f;
            var em = ps.emission; em.rateOverTime = 22f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1.4f; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0, 1) }); col.color = g;
            aura.GetComponent<ParticleSystemRenderer>().sharedMaterial = FX.NewMat(A.softDot, true, Color.white, 2f);
        }

        // ---------------- 队友（正式模型做好前，用 KayKit 骑士换色占位） ----------------
        void SpawnAlly(HeroId id, Vector3 pos)
        {
            var A = SliceAssets.I;
            var tripo = id == HeroId.Qinglan ? A.qinglanTripo : A.chengyinTripo;
            if (tripo && A.heroTripoController && A.heroModel) { SpawnAllyTripo(id, pos, tripo); return; }
            var root = new GameObject(id == HeroId.Qinglan ? "青岚" : "澄音");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            var model = SpawnModel(root.transform, A.heroModel, A.heroController);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                if (!r.name.StartsWith("Knight")) r.gameObject.SetActive(false);   // 去掉剑盾等武器
            Color tint = id == HeroId.Qinglan ? new Color(0.55f, 1f, 0.85f) : new Color(1f, 0.88f, 0.55f);
            Color em = id == HeroId.Qinglan ? new Color(0.05f, 0.22f, 0.17f) : new Color(0.24f, 0.18f, 0.05f);
            ApplyMat(model, CharMat(A.heroTexture, tint, em), null);
            model.transform.localScale = Vector3.one * 0.85f;
            HeroBase hb;
            if (id == HeroId.Qinglan)
            {
                var q = root.AddComponent<QinglanController>();
                q.Setup(model, Find(model.transform, "handslot.r"), Find(model.transform, "handslot.l"));
                hb = q;
            }
            else
            {
                var c = root.AddComponent<ChengyinController>();
                c.Setup(model, Find(model.transform, "handslot.r"), A.instrumentTripo, A.instrumentTripo ? TripoMat(A.instrumentAlbedo, null, Color.white, Color.black, 0.45f) : null);
                hb = c;
            }
            hb.rig = rig;
            hb.dmg.RefreshRenderers();
            HeroMarker(hb);
        }

        // Tripo 版青岚、澄音：人形模型直接用绯烈的动画控制器（同一套 KayKit 人形动画）
        void SpawnAllyTripo(HeroId id, Vector3 pos, GameObject prefab)
        {
            var A = SliceAssets.I;
            bool qing = id == HeroId.Qinglan;
            float height = qing ? 1.9f : 1.85f;
            var root = new GameObject(qing ? "青岚" : "澄音");
            root.transform.position = pos;
            var model = SpawnModel(root.transform, prefab, A.heroTripoController);
            var anim = model.GetComponent<Animator>();
            ApplyMat(model, ArenaBuilder.Lit(qing ? A.qinglanAlbedo : A.chengyinAlbedo, Color.white, 0.3f), null);
            float h = ModelHeight(model, anim);
            if (h > 0.01f) model.transform.localScale = Vector3.one * (height / h);
            Transform handR = anim.GetBoneTransform(HumanBodyBones.RightHand), handL = anim.GetBoneTransform(HumanBodyBones.LeftHand);

            // 青岚双枪：枪管顺着前臂方向，射击时伸手枪口就朝前（KayKit 的射击动作原本握的是弩，拿剑当参考会偏）
            if (qing && A.pistolTripo && anim.isHuman)
            {
                var mat = TripoMat(A.pistolAlbedo, null, Color.white, Color.black, 0.4f);
                var r = HandHeld.Attach(root.transform, anim, HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm, A.pistolTripo, mat, 0.45f);
                var l = HandHeld.Attach(root.transform, anim, HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, A.pistolTripo, mat, 0.45f);
                if (r) handR = r.transform;
                if (l) handL = l.transform;
            }

            HeroBase hb;
            if (qing) { var q = root.AddComponent<QinglanController>(); q.Setup(model, handR, handL); hb = q; }
            else { var c = root.AddComponent<ChengyinController>(); c.Setup(model, handR, A.instrumentTripo, A.instrumentTripo ? TripoMat(A.instrumentAlbedo, null, Color.white, Color.black, 0.45f) : null); hb = c; }
            root.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            hb.rig = rig;
            hb.dmg.RefreshRenderers();
            HeroMarker(hb);
        }

        // 脚下的职业色光圈：三人混战时一眼分清谁是谁
        static void HeroMarker(HeroBase h)
        {
            var go = new GameObject("Marker");
            go.transform.SetParent(h.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = Telegraph.Sector(0.72f, 0.92f, 360f, 48);
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var m = new Material(SliceAssets.I.telegraphShader);
            m.SetColor("_Color", new Color(h.Color.r, h.Color.g, h.Color.b, 0.75f));
            m.SetFloat("_Progress", 1f);
            m.SetFloat("_Sides", 0f);
            r.sharedMaterial = m;
            go.AddComponent<MarkerSpin>();
        }

        // ---------------- 残像 ----------------
        // 倒地救援时刷出的残像：0 轻波 / 1 弱巨浪 / 2 巨浪 / 3 强巨浪（怒涛级在竞技场里用最强巨浪代替）
        public static string ResidualName(int tier) => tier <= 0 ? "轻波" : tier == 1 ? "较弱巨浪" : tier == 2 ? "巨浪" : "强化巨浪";

        public void SpawnResidual(Vector3 pos, int tier, HeroBase owner)
        {
            tier = Mathf.Clamp(tier, 0, 3);
            float[] hp = { 1f, 4f, 6f, 9f }, dm = { 1f, 1.8f, 2.4f, 3f }, size = { 1f, 1.3f, 1.45f, 1.6f };
            SpawnMinion(pos, hp[tier], dm[tier], size[tier], owner);
            if (owner) Combat.Toast($"{owner.HeroName}的遗留残像出现了 · 击败它恢复等级", new Color(1f, 0.6f, 0.4f));
        }

        public void SpawnMinion(Vector3 pos) => SpawnMinion(pos, 1f, 1f, 1f, null);

        // 第四批兵种（MinionController.Kind）：用 C5–C10 模型和骷髅小兵的人形动画；没导入第四批时退回残像外观（行为照常）
        static readonly Dictionary<string, Material> variantMats = new Dictionary<string, Material>();
        public MinionController SpawnVariant(Vector3 pos, MinionController.Kind kind, float hpMult, float dmgMult)
        {
            var A = SliceAssets.I;
            string id = null, name = "残像";
            float height = 2f;
            switch (kind)
            {
                case MinionController.Kind.Shield: id = "shield_husk"; name = "残像·盾卫"; height = 2.3f; break;
                case MinionController.Kind.Bloat: id = "bloat_husk"; name = "残像·潮囊"; height = 2.2f; break;
                case MinionController.Kind.Chanter: id = "chanter_husk"; name = "残像·咏唱者"; height = 2.1f; break;
                case MinionController.Kind.Duelist: id = "tideblade_duelist"; name = "巨浪·潮刃剑士"; height = 2.8f; break;
                case MinionController.Kind.Javelin: id = "coral_javelineer"; name = "巨浪·珊瑚投矛手"; height = 2.8f; break;
                case MinionController.Kind.Priest: id = "tide_priest"; name = "巨浪·唤潮祭司"; height = 2.8f; break;
            }
            bool big = kind == MinionController.Kind.Duelist || kind == MinionController.Kind.Javelin || kind == MinionController.Kind.Priest;
            float size = big ? 1.4f : 1f;
            var m = id != null ? A.B4(id) : null;
            MinionController mc;
            if (m == null || !m.model || !A.minionTripoController)
            {
                SpawnMinion(pos, hpMult, dmgMult, size, null);
                mc = MinionController.Last;
            }
            else
            {
                if (!variantMats.TryGetValue(id, out var mat) || !mat)
                    variantMats[id] = mat = TripoMat(m.albedo, m.emission, Color.white, (id == "frost_witch" ? new Color(0.7f, 0.9f, 1f) : new Color(0.25f, 1f, 0.95f)) * 3f, 0.4f);
                var root = new GameObject(name);
                root.transform.position = pos;
                root.transform.rotation = Quaternion.LookRotation(Combat.player ? (Combat.player.transform.position - pos).normalized : Vector3.back);
                var model = SpawnModel(root.transform, m.model, A.minionTripoController);
                ApplyMat(model, mat, null);
                float h = ModelHeight(model, model.GetComponent<Animator>());
                if (h > 0.01f) model.transform.localScale = Vector3.one * (height / h);
                mc = root.AddComponent<MinionController>();
                mc.Setup(model, hpMult, dmgMult, size, null);
                mc.dmg.RefreshRenderers();
            }
            if (mc) { mc.SetKind(kind); mc.gameObject.name = name; }
            return mc;
        }

        // 残像·刺鳐（远程小怪）：没有骨骼的悬浮模型。头朝前、翼展约 2.4 米
        static Material rayMat;
        public RayController SpawnRay(Vector3 pos, float hpMult)
        {
            var p = SliceAssets.I.MapProp("17_ray_husk");
            if (p == null || !p.model) return null;
            var root = new GameObject("残像·刺鳐");
            root.transform.position = pos;
            var holder = new GameObject("Body").transform;
            holder.SetParent(root.transform, false);
            var m = Instantiate(p.model, holder).transform;
            m.localPosition = Vector3.zero; m.localRotation = Quaternion.identity; m.localScale = Vector3.one;
            var verts = new System.Collections.Generic.List<Vector3>();
            foreach (var mf in m.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var M = holder.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) verts.Add(M.MultiplyPoint3x4(v));
            }
            var s = WeaponFit.Analyze(verts.ToArray(), false);
            // 头尾方向：尾巴那一端细（横向跨度小），头朝另一端
            float spanLo = 0f, spanHi = 0f;
            foreach (var v in verts)
            {
                float tt = Vector3.Dot(v - s.center, s.axis);
                float w = Mathf.Abs(Vector3.Dot(v - s.center, s.wide));
                if (tt < s.min + s.Len * 0.3f) spanLo = Mathf.Max(spanLo, w);
                if (tt > s.max - s.Len * 0.3f) spanHi = Mathf.Max(spanHi, w);
            }
            Vector3 head = spanHi > spanLo ? s.axis : -s.axis;
            Vector3 up = Vector3.Cross(s.axis, s.wide).normalized;
            if (up.y < 0f) up = -up;
            Quaternion R = Quaternion.Inverse(Quaternion.LookRotation(head, up));
            float span = 0f;
            foreach (var v in verts) span = Mathf.Max(span, Mathf.Abs(Vector3.Dot(v - s.center, s.wide)));
            float scale = 1.2f / Mathf.Max(1e-4f, span);
            m.localRotation = R;
            m.localScale = Vector3.one * scale;
            m.localPosition = -(R * s.center) * scale;
            if (!rayMat)
            {
                rayMat = ArenaBuilder.Lit(p.albedo, Color.white, 0.55f);
                if (p.emission) { rayMat.SetTexture("_EmissionMap", p.emission); rayMat.SetColor("_EmissionColor", new Color(0.25f, 1f, 0.95f) * 3f); }
            }
            foreach (var r in m.GetComponentsInChildren<Renderer>(true)) { r.sharedMaterial = rayMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
            if (Combat.player) root.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Combat.player.transform.position - pos, Vector3.up).normalized + Vector3.forward * 0.001f);
            var rc = root.AddComponent<RayController>();
            rc.Setup(holder, hpMult);
            rc.dmg.RefreshRenderers();
            return rc;
        }

        public void SpawnMinion(Vector3 pos, float hpMult, float dmgMult, float size, HeroBase owner)
        {
            var A = SliceAssets.I;
            if (SpawnMinionTripo(pos, hpMult, dmgMult, size, owner)) return;
            var root = new GameObject("残像");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.LookRotation(Combat.player ? (Combat.player.transform.position - pos).normalized : Vector3.back);
            var model = SpawnModel(root.transform, A.minionModel, A.minionController);
            AttachWeapon(model, A.bladeModel);
            var body = CharMat(A.skeletonTexture, new Color(0.5f, 0.46f, 0.68f), new Color(0.05f, 0.01f, 0.1f));
            var eyes = CharMat(A.skeletonTexture, new Color(0.6f, 0.3f, 1f), new Color(1.2f, 0.5f, 3f));
            ApplyMat(model, body, eyes);
            model.transform.localScale = Vector3.one * 0.9f * size;
            var mc = root.AddComponent<MinionController>();
            mc.Setup(model, hpMult, dmgMult, size, owner);
            mc.dmg.RefreshRenderers();
        }
    }

    public class MarkerSpin : MonoBehaviour
    {
        Renderer r;
        HeroBase h;
        void Awake() { r = GetComponent<Renderer>(); h = GetComponentInParent<HeroBase>(); }
        void LateUpdate()
        {
            if (!h) return;
            r.enabled = !h.Downed;
            float s = h.IsControlled ? 1.15f + 0.05f * Mathf.Sin(Time.time * 4f) : 1f;
            transform.localScale = new Vector3(s, 1f, s);
            // 角色模型缩放会带歪光圈高度，这里固定贴地
            Vector3 p = transform.position; p.y = 0.06f; transform.position = p;
        }
    }
}
