using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlackTide.EditorTools
{
    // 只在显式执行此工具时注入；不会进入游戏构建，也不保存测试场景改动。
    public static class MotionNaturalnessProbe
    {
        const string Key = "BlackTide.MotionProbe";
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-btMotionStage");
            SessionState.SetString(Key + ".stage", i >= 0 && i + 1 < args.Length ? args[i + 1] : "baseline");
            SessionState.SetBool(Key, true);
            SessionState.SetFloat(Key + ".started", (float)EditorApplication.timeSinceStartup);
            SessionState.SetBool(Key + ".done", false);
            EditorSceneManager.OpenScene("Assets/BlackTide/Scenes/BlackTide_Slice.unity");
            Attach();
            EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Attach()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }
        static void Poll()
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (SessionState.GetBool(Key + ".done", false) && !EditorApplication.isPlaying)
            {
                SessionState.SetBool(Key, false);
                EditorApplication.Exit(SessionState.GetInt(Key + ".exit", 0));
                return;
            }
            if (EditorApplication.isPlaying && Combat.player && !UnityEngine.Object.FindObjectOfType<MotionProbeDriver>())
                new GameObject("MotionProbe").AddComponent<MotionProbeDriver>().Init(SessionState.GetString(Key + ".stage", "baseline"));
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + ".started", 0f) > 210)
                Finish(false, "timeout");
        }
        public static void Finish(bool ok, string detail)
        {
            Debug.Log("MOTION_PROBE RESULT=" + (ok ? "PASS" : "FAIL") + " " + detail);
            SessionState.SetInt(Key + ".exit", ok ? 0 : 1);
            SessionState.SetBool(Key + ".done", true);
            EditorApplication.ExitPlaymode();
        }
    }

    public class MotionProbeDriver : MonoBehaviour
    {
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        HeroBase hero;
        Damageable target;
        Camera cam;
        RenderTexture rt;
        Texture2D tex;
        string folder, stage;
        int frame, firstFrame, errors, caseIndex = -1;
        Vector3 previous;
        readonly StringBuilder rows = new StringBuilder("frame,case,time,state,clip,speed,x,z,yaw,leftY,rightY,targetHp,leftX,leftZ,rightX,rightZ,animationRate,invulnerable,walkHeld\n");
        readonly string[] cases = { "run", "slow", "fast", "reverse", "stop", "dodge_forward", "dodge_back", "dodge_left", "dodge_right", "combo", "chase", "cancel", "qinglan", "chengyin", "generic", "ctrl_walk", "wall", "skill", "ult", "weapon_skill", "turn_attack", "speed_debuff", "ai_walk_reset" };
        const int CaseFrames = 120;
        bool ready;
        float minHp;
        float maxTravel;
        int lastCaseExclusive;
        GameObject wall;
        MethodInfo Method(string name) => hero.GetType().GetMethod(name, Flags) ?? typeof(HeroBase).GetMethod(name, Flags);
        void Call(string name, params object[] args) { Method(name)?.Invoke(hero, args); }
        void Field(string name, object value) => typeof(HeroBase).GetField(name, Flags)?.SetValue(hero, value);
        object Field(string name) => typeof(HeroBase).GetField(name, Flags)?.GetValue(hero);
        public void Init(string label)
        {
            stage = label;
            string[] args = Environment.GetCommandLineArgs();
            int argument = Array.IndexOf(args, "-btMotionFrom");
            if (argument >= 0 && argument + 1 < args.Length && int.TryParse(args[argument + 1], out int start))
                frame = firstFrame = Mathf.Clamp(start, 0, cases.Length - 1) * CaseFrames;
            argument = Array.IndexOf(args, "-btMotionUntil");
            lastCaseExclusive = cases.Length;
            if (argument >= 0 && argument + 1 < args.Length && int.TryParse(args[argument + 1], out int end))
                lastCaseExclusive = Mathf.Clamp(end, firstFrame / CaseFrames + 1, cases.Length);
            folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", "motion_20261011", label));
            Directory.CreateDirectory(folder);
            Application.logMessageReceived += Log;
            hero = Combat.player;
            foreach (var h in Combat.heroes) h.enabled = false;
            foreach (var e in Combat.enemies.ToArray()) if (e) e.gameObject.SetActive(false);
            Combat.enemies.Clear(); Combat.threats.Clear();
            foreach (var c in FindObjectsOfType<Canvas>()) c.gameObject.SetActive(false);
            if (SliceGame.I) SliceGame.I.enabled = false;
            if (RunDirector.I) RunDirector.I.enabled = false;
            HeroBase.God = true;
            Feel.SetBaseScale(1f);
            Time.captureDeltaTime = 1f / 60f;
            Application.targetFrameRate = 60;
            var obj = new GameObject("ProbeTarget"); target = obj.AddComponent<Damageable>();
            target.team = Team.Enemy; target.maxHp = target.hp = 100000f; target.radius = 0.6f;
            minHp = target.hp;
            cam = Camera.main;
            var rig = cam.GetComponent<CameraRig>(); if (rig) rig.enabled = false;
            cam.fieldOfView = 36f;
            rt = new RenderTexture(640, 480, 24); rt.Create();
            tex = new Texture2D(640, 480, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            foreach (var h in Combat.heroes)
            {
                Debug.Log($"MOTION_CLIPS hero={h.HeroName} " + string.Join("|", h.anim.animator.runtimeAnimatorController.animationClips.Where(c => c.name.StartsWith("Dodge") || c.name.StartsWith("Running") || c.name.StartsWith("Walking")).Select(c => c.name + ":" + c.length.ToString("F3"))));
                h.gameObject.SetActive(h == hero);
            }
            Debug.Log("MOTION_PROBE START stage=" + stage + " folder=" + folder);
            ready = true;
        }
        void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception) errors++; }
        void ResetCase(int index)
        {
            if (wall) { wall.SetActive(false); Destroy(wall); }
            if (index == 12) hero = Combat.heroes.First(h => h.Id == HeroId.Qinglan);
            if (index == 13) hero = Combat.heroes.First(h => h.Id == HeroId.Chengyin);
            if (index == 14) hero = Combat.heroes.First(h => h.Id == HeroId.Feilie);
            Combat.player = hero;
            foreach (var h in Combat.heroes) h.gameObject.SetActive(h == hero);
            var cc = hero.GetComponent<CharacterController>(); cc.enabled = false;
            hero.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); cc.enabled = true;
            hero.attackHeld = false; hero.interactHeld = false;
            hero.bufAttack = hero.bufDodge = hero.bufWeapon = hero.bufSkill = hero.bufUlt = hero.bufEcho = -9f;
            hero.relayTarget = null; hero.moveIn = Vector3.zero;
            Field("moveVelocity", Vector3.zero); Field("moveDamping", Vector3.zero); hero.walkHeld = false;
            hero.AimPoint = Vector3.forward * 8f;
            Field("lockedTarget", null);
            Call("Enter", HeroBase.State.Move);
            target.transform.position = new Vector3(0f, 0f, index == 10 ? 5f : 2.4f);
            target.dead = false;
            Combat.enemies.Clear();
            if (index >= 9 && index <= 11 || index == 14) Combat.enemies.Add(target);
            if (index >= 17 && index <= 20) Combat.enemies.Add(target);
            if (index == 10) Field("lockedTarget", target);
            hero.moveSpeed = 6.4f;
            if (index == 1) hero.moveSpeed = 2.6f;
            if (index == 2) hero.moveSpeed = 9f;
            if (index == 14)
            {
                hero.kit.weapons[1] = Weapons.Make(Weapons.All.First(d => d.type == WType.重武器), 2);
                hero.kit.active = 1; hero.RefreshWeapon();
            }
            if (index >= 15) { hero.kit.active = 0; hero.RefreshWeapon(); }
            if (index == 15) hero.walkHeld = true;
            if (index == 16)
            {
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "ProbeWall"; wall.transform.position = new Vector3(0, 1.5f, 3f);
                wall.transform.localScale = new Vector3(6f, 3f, 0.3f);
            }
            if (index == 17) hero.skillReadyAt = 0f;
            if (index == 18) { hero.level = 12; hero.energy = hero.energyMax; }
            if (index == 19) hero.kit.Active.skillReadyAt = 0f;
            if (index == 20)
            {
                hero.AimPoint = Vector3.back * 8f;
                target.transform.position = Vector3.back * 2.4f;
            }
            if (index == 21) hero.AddMod("speed", "motion_probe", -0.6f, 3f);
            if (index == 22) { hero.walkHeld = true; hero.enabled = true; HeroBase.Autopilot = true; }
            previous = hero.transform.position;
            Debug.Log("MOTION_CASE " + cases[index]);
        }
        void Update()
        {
            if (!ready) return;
            try
            {
                int ci = frame / CaseFrames, f = frame % CaseFrames;
                if (ci >= lastCaseExclusive)
                {
                    File.WriteAllText(Path.Combine(folder, "motion.csv"), rows.ToString());
                    File.WriteAllText(Path.Combine(folder, "result.txt"), $"stage={stage} frames={frame - firstFrame} errors={errors} targetDamage={100000f - minHp:F1}");
                    ready = false;
                    bool observed = lastCaseExclusive <= 5 ? maxTravel > 1f : minHp < 100000f;
                    MotionNaturalnessProbe.Finish(errors == 0 && observed, $"frames={frame - firstFrame} errors={errors} damage={100000f - minHp:F1} travel={maxTravel:F2}"); return;
                }
                if (ci != caseIndex) { caseIndex = ci; ResetCase(ci); }
                // 最后一组走真实 HeroBase.Update，验证交给 AI 后不会残留玩家的慢走输入。
                if (ci == 22) return;
                hero.moveIn = ci <= 2 || ci >= 12 && ci <= 13 ? Vector3.forward : Vector3.zero;
                if (ci == 15 || ci == 16 || ci == 21) hero.moveIn = Vector3.forward;
                if (ci == 3) hero.moveIn = f < 60 ? Vector3.forward : Vector3.back;
                if (ci == 4) hero.moveIn = f < 60 ? Vector3.forward : Vector3.zero;
                if (ci >= 5 && ci <= 8 && f == 20)
                {
                    hero.moveIn = ci == 5 ? Vector3.forward : ci == 6 ? Vector3.back : ci == 7 ? Vector3.left : Vector3.right;
                    hero.bufDodge = Time.time;
                }
                if ((ci >= 9 && ci <= 11 || ci == 14) && f == 20) hero.bufAttack = Time.time;
                hero.attackHeld = ci == 9 && f < 110;
                if (ci == 11 && f == 32) { hero.moveIn = Vector3.right; hero.bufDodge = Time.time; }
                if (ci == 11 && f == 56) hero.bufAttack = Time.time;
                if (f == 20)
                {
                    if (ci == 17) hero.bufSkill = Time.time;
                    if (ci == 18) hero.bufUlt = Time.time;
                    if (ci == 19) hero.bufWeapon = Time.time;
                    if (ci == 20) hero.bufAttack = Time.time;
                }
                Field("stateT", (float)Field("stateT") + Time.deltaTime);
                Call("BeginMotionFrame");
                if (hero.state == HeroBase.State.Move) Call("TickMove");
                else if (hero.state == HeroBase.State.Dodge) Call("TickDodge");
                else if (hero.state == HeroBase.State.Attack)
                {
                    bool generic = (bool)Field("gAttacking");
                    Call(generic ? "GenericTickBasic" : "TickAttack");
                }
                else if (hero.state == HeroBase.State.Skill) Call("TickSkill");
                else if (hero.state == HeroBase.State.Ult) Call("TickUlt");
                else if (hero.state == HeroBase.State.WSkill) Call("TickWSkill");
                Call("EndMotionFrame");
                minHp = Mathf.Min(minHp, target.hp);
            }
            catch (Exception ex) { ready = false; Debug.LogException(ex); MotionNaturalnessProbe.Finish(false, ex.ToString()); }
        }
        void LateUpdate()
        {
            if (!ready) return;
            Vector3 p = hero.transform.position;
            maxTravel = Mathf.Max(maxTravel, p.magnitude);
            var an = hero.anim.animator;
            Vector3 l = an.isHuman ? an.GetBoneTransform(HumanBodyBones.LeftFoot).position : p;
            Vector3 r = an.isHuman ? an.GetBoneTransform(HumanBodyBones.RightFoot).position : p;
            float speed = Vector3.Distance(p, previous) / Mathf.Max(0.0001f, Time.deltaTime);
            rows.AppendLine(FormattableString.Invariant($"{frame},{cases[caseIndex]},{(float)Field("stateT"):F4},{hero.state},{hero.anim.current},{speed:F4},{p.x:F4},{p.z:F4},{hero.transform.eulerAngles.y:F2},{l.y:F4},{r.y:F4},{target.hp:F1},{l.x:F4},{l.z:F4},{r.x:F4},{r.z:F4},{an.speed:F4},{hero.Invulnerable},{hero.walkHeld}"));
            previous = p;
            if (frame % 6 == 0)
            {
                cam.transform.position = p + new Vector3(5.5f, 3.2f, -6.8f);
                cam.transform.LookAt(p + Vector3.up * 1.05f);
                cam.Render();
                var old = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = old;
                File.WriteAllBytes(Path.Combine(folder, $"{caseIndex:00}_{cases[caseIndex]}_{frame % CaseFrames:000}.jpg"), tex.EncodeToJPG(88));
            }
            frame++;
        }
        void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            Time.captureDeltaTime = 0f;
            if (cam) cam.targetTexture = null;
            if (rt) { rt.Release(); Destroy(rt); }
            if (tex) Destroy(tex);
        }
    }
}
