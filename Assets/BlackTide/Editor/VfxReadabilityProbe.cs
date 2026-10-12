using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace BlackTide.EditorTools
{
    public static class VfxReadabilityProbe
    {
        const string Key="BT.VfxProbe";
        public static void Baseline() { Begin("before"); }
        public static void Revised() { Begin("after"); }
        public static void VerifyAndBuild() { SessionState.SetBool(Key+".build",true); Begin("after"); }
        public static void IntegrationAndBuild() { SessionState.SetBool(Key+".build",true); Begin("integration"); }
        static void Begin(string stage)
        {
            SessionState.SetString(Key+".stage",stage); SessionState.SetBool(Key,true);
            SessionState.SetBool(Key+".done",false); SessionState.SetFloat(Key+".start",(float)EditorApplication.timeSinceStartup);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Attach(); EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Attach() { EditorApplication.update-=Poll; EditorApplication.update+=Poll; }
        static void Poll()
        {
            if(!SessionState.GetBool(Key,false)) return;
            if(SessionState.GetBool(Key+".done",false) && !EditorApplication.isPlaying)
            {
                int code=SessionState.GetInt(Key+".exit",1);
                bool build=SessionState.GetBool(Key+".build",false);
                if(build && code==0 && Stage=="after")
                { Begin("integration"); return; }
                SessionState.SetBool(Key,false); SessionState.SetBool(Key+".build",false);
                if(build && code==0)
                {
                    bool built=false;
                    Application.LogCallback cb=(m,s,t)=>{ if(m.StartsWith("BUILD_RESULT Succeeded")) built=true; };
                    Application.logMessageReceived+=cb;
                    try { BuildGame.Build(); code=built?0:1; }
                    catch(Exception e) { Debug.LogException(e); code=1; }
                    finally { Application.logMessageReceived-=cb; }
                }
                EditorApplication.Exit(code); return;
            }
            if(EditorApplication.isPlaying)
            {
                if(Stage=="integration")
                { if(!UnityEngine.Object.FindObjectOfType<VfxIntegrationDriver>()) new GameObject("VfxIntegrationProbe").AddComponent<VfxIntegrationDriver>(); }
                else if(!UnityEngine.Object.FindObjectOfType<VfxReadabilityDriver>()) new GameObject("VfxReadabilityProbe").AddComponent<VfxReadabilityDriver>();
            }
            if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".start",0)>150) Finish(false,"timeout");
        }
        public static string Stage => SessionState.GetString(Key+".stage","before");
        public static void Finish(bool pass,string details)
        {
            Debug.Log("VFX_PROBE RESULT="+(pass?"PASS":"FAIL")+" "+details);
            SessionState.SetBool(Key+".done",true); SessionState.SetInt(Key+".exit",pass?0:1); EditorApplication.ExitPlaymode();
        }
    }
    public sealed class VfxReadabilityDriver : MonoBehaviour
    {
        Camera cam; Damageable target; string folder; int errors,frames; GameObject figure;
        void Log(string m,string s,LogType t) { if(t==LogType.Error || t==LogType.Exception) errors++; }
        IEnumerator Start()
        {
            Application.logMessageReceived+=Log; Time.captureFramerate=60; Time.timeScale=1;
            folder="Captures/vfx_CX007/"+VfxReadabilityProbe.Stage; Directory.CreateDirectory(folder);
            var cg=new GameObject("Main Camera"); cg.tag="MainCamera"; cam=cg.AddComponent<Camera>();
            cam.transform.position=new Vector3(0,8,-10); cam.transform.LookAt(new Vector3(0,.7f,0));
            cam.orthographic=true; cam.orthographicSize=5.5f; cam.backgroundColor=new Color(.02f,.028f,.04f);
            cam.clearFlags=CameraClearFlags.SolidColor; cam.allowHDR=true;
            cg.AddComponent<AudioListener>();
            cg.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
            var light=new GameObject("Key").AddComponent<Light>(); light.type=LightType.Directional;
            light.transform.rotation=Quaternion.Euler(50,-30,0); light.intensity=1.1f;
            RenderSettings.ambientLight=new Color(.22f,.25f,.3f);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name="ProbeFloor";
            var gm=new Material(Shader.Find("Universal Render Pipeline/Lit")); gm.color=new Color(.075f,.09f,.12f);
            ground.GetComponent<Renderer>().sharedMaterial=gm; ground.transform.localScale=Vector3.one*3;
            figure=GameObject.CreatePrimitive(PrimitiveType.Capsule); figure.name="ReferenceBody";
            figure.transform.position=Vector3.up; figure.transform.localScale=new Vector3(.65f,1,.65f);
            var fm=new Material(Shader.Find("Universal Render Pipeline/Lit")); fm.color=new Color(.22f,.28f,.34f);
            figure.GetComponent<Renderer>().sharedMaterial=fm; target=figure.AddComponent<Damageable>(); target.enabled=false;
            FX.Init(); FX.Style=FX.VfxStyle.Neon; FX.Comic=false;
            typeof(FX).GetMethod("ApplyStyle",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            var grade=new GameObject("Grade").AddComponent<CyberGrade>(); grade.Build();
            // Render the actual screen layer into the same camera capture (no omission of the filter).
            foreach(var c in grade.GetComponentsInChildren<Canvas>())
            { c.renderMode=RenderMode.ScreenSpaceCamera; c.worldCamera=cam; c.planeDistance=1f; }
            yield return null;
            foreach(string name in new[]{"slash","fire","sound","shield","lightning","frost","burst"})
            {
                UnityEngine.Random.InitState(1407); figure.SetActive(name=="shield" || name=="slash");
                Capture(name+"_empty");
                switch(name)
                {
                    case "slash": FX.SlashArc(Vector3.up*.9f,Quaternion.identity,.8f,3.2f,140,.3f,new Color(.05f,.8f,1f),1); break;
                    case "fire": FX.FireField(Vector3.zero,2.4f,2.6f,new Color(1f,.4f,.06f)); break;
                    case "sound": FX.SoundFan(new Vector3(0,0,-2),Vector3.forward,3.5f,new Color(.15f,1,.75f)); break;
                    case "shield": FX.HexShield(target,1.25f,2.6f,new Color(.2f,.65f,1)); break;
                    case "lightning": FX.BoltStrip(new Vector3(-2,.4f,-1),new Vector3(2,2,1),new Color(.1f,.45f,1)); FX.Strike(Vector3.zero,new Color(.1f,.45f,1)); break;
                    case "frost": FX.FrozenGround(Vector3.zero,2.4f,new Color(.25f,.7f,1),2.6f); FX.IceBurst(Vector3.zero,new Color(.25f,.7f,1)); break;
                    case "burst": FX.BigBurst(Vector3.zero,2.4f,new Color(1,.45f,.1f)); break;
                }
                for(int f=1;f<=168;f++)
                {
                    yield return null;
                    if(f==5 || f==15 || f==42 || f==120 || f==168) Capture(name+"_"+f.ToString("D3"));
                }
                foreach(var a in FindObjectsOfType<FxAnim>()) Destroy(a.gameObject);
                foreach(var a in FindObjectsOfType<FxFollow>()) Destroy(a.gameObject);
                // Other finite visual components must have expired by frame 168.
                foreach(var ps in FindObjectsOfType<ParticleSystem>()) ps.Clear();
                yield return null;
            }
            var shaderErrors=AssetDatabase.FindAssets("t:Shader",new[]{"Assets/BlackTide/Shaders"}).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<Shader>).Where(ShaderUtil.ShaderHasError).Select(s=>s.name).ToArray();
            File.WriteAllText(Path.Combine(folder,"result.txt"),"frames="+frames+" errors="+errors+" shaderErrors="+string.Join(",",shaderErrors)+"\nCamera=(0,8,-10), ortho=5.5, seed=1407, captureFramerate=60; preferences untouched.\n");
            VfxReadabilityProbe.Finish(errors==0 && shaderErrors.Length==0 && frames==42,"captures="+frames+" errors="+errors+" shaders="+shaderErrors.Length);
        }
        void Capture(string name)
        {
            var rt=new RenderTexture(960,540,24); var tex=new Texture2D(960,540,TextureFormat.RGB24,false);
            var old=RenderTexture.active; cam.targetTexture=rt; cam.Render(); RenderTexture.active=rt;
            tex.ReadPixels(new Rect(0,0,960,540),0,0); tex.Apply(); File.WriteAllBytes(Path.Combine(folder,name+".png"),tex.EncodeToPNG());
            RenderTexture.active=old; cam.targetTexture=null; Destroy(rt); Destroy(tex); frames++;
        }
        void OnDestroy() { Time.captureFramerate=0; Application.logMessageReceived-=Log; }
    }
    public sealed class VfxIntegrationDriver : MonoBehaviour
    {
        int errors; readonly System.Collections.Generic.List<string> checks=new System.Collections.Generic.List<string>();
        void Log(string m,string s,LogType type) { if(type==LogType.Error || type==LogType.Exception) errors++; }
        void Check(string name,bool pass) { checks.Add(name+"="+(pass?"PASS":"FAIL")); Debug.Log("VFX_CHECK "+checks.Last()); }
        void Style(FX.VfxStyle style)
        { FX.Style=style; FX.Comic=style==FX.VfxStyle.Comic; typeof(FX).GetMethod("ApplyStyle",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null); }
        IEnumerator Frames(int n) { for(int i=0;i<n;i++) yield return null; }
        void Call(string name,params object[] args) { typeof(Arsenal).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args); }
        void Capture(string name)
        {
            var cam=Camera.main; if(!cam) return;
            foreach(var canvas in FindObjectsOfType<Canvas>())
            { if(canvas.renderMode==RenderMode.ScreenSpaceOverlay) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=cam; canvas.planeDistance=.8f; } }
            var rt=new RenderTexture(1280,720,24); var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);
            var old=RenderTexture.active; cam.targetTexture=rt; cam.Render(); RenderTexture.active=rt;
            tex.ReadPixels(new Rect(0,0,1280,720),0,0); tex.Apply();
            File.WriteAllBytes("Captures/vfx_CX007/integration/"+name+".png",tex.EncodeToPNG());
            RenderTexture.active=old; cam.targetTexture=null; Destroy(rt); Destroy(tex);
        }
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject); Directory.CreateDirectory("Captures/vfx_CX007/integration");
            Application.logMessageReceived+=Log; Time.captureFramerate=60;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/BlackTide/Scenes/MainMenu.unity",new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
            yield return Frames(30);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/BlackTide/Scenes/BlackTide_Run.unity",new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
            yield return Frames(100);
            var hero=Combat.player; Check("run_player",hero && Camera.main);
            if(!hero) { VfxReadabilityProbe.Finish(false,"run player absent"); yield break; }
            HeroBase.Autopilot=false; HeroBase.God=true; Feel.SetBaseScale(1);
            if(RunDirector.I) RunDirector.I.enabled=false;
            if(SliceGame.I) SliceGame.I.enabled=false;
            foreach(var t in FindObjectsOfType<Tide>()) t.enabled=false;
            foreach(var t in FindObjectsOfType<Hunters>()) t.enabled=false;
            foreach(var h in Combat.heroes) if(h) h.enabled=false;
            foreach(var e in Combat.enemies.ToArray()) if(e) e.gameObject.SetActive(false);
            Combat.enemies.Clear(); Combat.bosses.Clear(); Combat.threats.Clear();
            var pos=hero.transform.position; UnityEngine.Random.InitState(1407);
            foreach(var style in new[]{FX.VfxStyle.Neon,FX.VfxStyle.Realistic,FX.VfxStyle.Comic,FX.VfxStyle.Neon})
            {
                Style(style); FX.SlashArc(pos+Vector3.up,Quaternion.identity,.7f,2.5f,140,.2f,new Color(.1f,.8f,1),1);
                yield return Frames(5);
                string expected=style==FX.VfxStyle.Neon?"BlackTide/SlashNeon":style==FX.VfxStyle.Comic?"BlackTide/SlashInk":"BlackTide/Slash";
                var slash=FindObjectsOfType<SlashFx>().FirstOrDefault(s=>s.gameObject.activeSelf);
                Check("pooled_slash_"+style,slash && slash.GetComponent<MeshRenderer>().sharedMaterial.shader.name==expected);
                var ps=GameObject.Find("FX/Sparks").GetComponent<ParticleSystemRenderer>();
                Check("particle_style_"+style,ps.sharedMaterial.shader.name==(style==FX.VfxStyle.Neon?"BlackTide/Glitch":"BlackTide/Additive"));
                Capture("style_"+style); yield return Frames(35);
            }
            Style(FX.VfxStyle.Neon);
            var dummy=GameObject.CreatePrimitive(PrimitiveType.Capsule); dummy.name="VentTarget"; dummy.transform.position=pos+Vector3.forward*3+Vector3.up;
            var target=dummy.AddComponent<Damageable>(); target.team=Team.Enemy; target.hp=target.maxHp=10000; target.enabled=false; Combat.enemies.Add(target);
            hero.rogue.levels.Clear(); hero.rogue.levels["vents"]=1;
            Call("TickVentSpawn",hero); yield return Frames(5);
            Check("vent_spawns_persistent_fire",FindObjectsOfType<ElementVfx>().Count(f=>f.name=="Element_Fire")==1);
            float hp=target.hp; Call("TickVents"); yield return Frames(2);
            Check("actual_vent_damage",target.hp<hp);
            Check("vent_tick_no_ring",!FindObjectsOfType<FxAnim>().Any(a=>a.name=="Ring"));
            FX.HexShield(hero.dmg,1.15f,2.6f,new Color(.2f,.65f,1,.65f));
            FX.SoundPulse(pos+Vector3.left*3,3,new Color(.15f,1,.75f));
            ElementVfx.Spawn(ElementVfx.Shape.Vortex,pos+Vector3.right*3,Vector3.forward,2.8f,2.6f,new Color(.2f,.8f,.95f));
            FX.BoltStrip(pos+Vector3.up*3,pos+Vector3.forward*2+Vector3.up,new Color(.1f,.45f,1));
            yield return Frames(10); Capture("run_elements");
            var shield=FindObjectsOfType<ElementVfx>().FirstOrDefault(f=>f.name=="Element_Shield");
            hero.transform.position+=Vector3.right*.5f; yield return Frames(2);
            Check("shield_follows_actual_hero",shield && Vector3.Distance(shield.transform.position,hero.dmg.Center)<.02f);
            var volume=CyberGrade.I.GetComponentInChildren<Volume>(); float glow=volume.sharedProfile.components.OfType<Bloom>().Single().intensity.value;
            hero.dmg.hp=hero.dmg.maxHp*.2f; CyberGrade.Pulse(1); yield return Frames(10); Capture("real_low_health_warning");
            Check("no_random_filter_images",CyberGrade.I.GetComponentsInChildren<RawImage>().Length==0);
            Check("no_danger_exposure_pulse",Mathf.Approximately(glow,volume.sharedProfile.components.OfType<Bloom>().Single().intensity.value));
            Check("no_camera_chroma",volume.sharedProfile.components.OfType<ChromaticAberration>().Single().intensity.value==0);
            yield return Frames(180); Check("envelopes_expire",FindObjectsOfType<ElementVfx>().Length==0);
            for(int cycle=0;cycle<3;cycle++)
            {
                for(int i=0;i<8;i++) ElementVfx.Spawn(ElementVfx.Shape.Fire,pos,Vector3.forward,1,.15f,new Color(1,.4f,.05f));
                yield return Frames(18);
                Check("repeat_cleanup_"+cycle,FindObjectsOfType<ElementVfx>().Length==0);
            }
            Check("runtime_errors",errors==0);
            File.WriteAllLines("Captures/vfx_CX007/integration/checks.txt",checks);
            VfxReadabilityProbe.Finish(checks.All(s=>s.EndsWith("PASS")),"integration checks="+checks.Count+" errors="+errors);
        }
        void OnDestroy() { Time.captureFramerate=0; Application.logMessageReceived-=Log; }
    }
}
