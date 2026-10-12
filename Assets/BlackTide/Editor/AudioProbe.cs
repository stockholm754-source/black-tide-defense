using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BlackTide.EditorTools
{
    public static class AudioProbe
    {
        const string Key="BT.AudioProbe";
        public static void RunAndBuild() { SessionState.SetBool(Key+".build",true); Run(); }
        public static void Run()
        {
            foreach(var p in Directory.GetFiles("Assets/BlackTide/Resources/SFX","*.wav")) AssetDatabase.ImportAsset(p.Replace('\\','/'),ImportAssetOptions.ForceUpdate);
            foreach(var p in Directory.GetFiles("Assets/BlackTide/Resources/Audio/Music","*.ogg")) AssetDatabase.ImportAsset(p.Replace('\\','/'),ImportAssetOptions.ForceUpdate);
            SessionState.SetBool(Key,true); SessionState.SetBool(Key+".done",false);
            SessionState.SetFloat(Key+".start",(float)EditorApplication.timeSinceStartup);
            EditorSceneManager.OpenScene("Assets/BlackTide/Scenes/MainMenu.unity"); Attach(); EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Attach() { EditorApplication.update-=Poll; EditorApplication.update+=Poll; }
        static void Poll()
        {
            if(!SessionState.GetBool(Key,false)) return;
            if(SessionState.GetBool(Key+".done",false) && !EditorApplication.isPlaying)
            {
                SessionState.SetBool(Key,false); int code=SessionState.GetInt(Key+".exit",1);
                bool build=SessionState.GetBool(Key+".build",false); SessionState.SetBool(Key+".build",false);
                if(build && code==0)
                {
                    bool built=false;
                    Application.LogCallback result=(message,stack,type)=> { if(message.StartsWith("BUILD_RESULT Succeeded")) built=true; };
                    Application.logMessageReceived+=result;
                    try { BuildGame.Build(); code=built ? 0 : 1; }
                    catch(Exception e) { Debug.LogException(e); code=1; }
                    finally { Application.logMessageReceived-=result; }
                }
                EditorApplication.Exit(code); return;
            }
            if(EditorApplication.isPlaying && GameAudio.I && !UnityEngine.Object.FindObjectOfType<AudioProbeDriver>()) new GameObject("AudioProbe").AddComponent<AudioProbeDriver>();
            if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".start",0)>150) Finish(false,"timeout");
        }
        public static void Finish(bool pass,string detail)
        {
            Debug.Log("AUDIO_PROBE RESULT="+(pass ? "PASS" : "FAIL")+" "+detail);
            SessionState.SetBool(Key+".done",true); SessionState.SetInt(Key+".exit",pass ? 0 : 1); EditorApplication.ExitPlaymode();
        }
    }
    public sealed class AudioProbeDriver : MonoBehaviour
    {
        readonly List<string> checks=new List<string>(), failures=new List<string>();
        readonly List<string> events=new List<string>();
        readonly List<float> output=new List<float>();
        int errors;
        string folder;
        readonly string[] settings={"Master","Music","Effects"};
        readonly float[] old=new float[3];
        readonly bool[] had=new bool[3];
        PadSample sample;
        void Check(string name,bool pass) { string r=name+"="+(pass ? "PASS" : "FAIL"); checks.Add(r); if(!pass) failures.Add(name); Debug.Log("AUDIO_CHECK "+r); }
        void Log(string message,string stack,LogType type) { if(type==LogType.Error || type==LogType.Exception || message.StartsWith("AUDIO_MISSING")) errors++; }
        IEnumerator Wait(float t) { yield return new WaitForSecondsRealtime(t); }
        AudioSource[] Sources => GameAudio.I.GetComponents<AudioSource>();
        void ClearVoices() { foreach(var s in Sources) if(s.priority<200) s.Stop(); }
        void Meter()
        {
            var frame=new float[1024]; AudioListener.GetOutputData(frame,0); output.AddRange(frame);
        }
        void LateUpdate() { if(GameAudio.I) Meter(); }
        void Capture(string name)
        {
            var cam=Camera.main; if(!cam) return;
            var rt=new RenderTexture(960,540,24); var tex=new Texture2D(960,540,TextureFormat.RGB24,false);
            cam.targetTexture=rt; cam.Render(); RenderTexture.active=rt; tex.ReadPixels(new Rect(0,0,960,540),0,0); tex.Apply();
            File.WriteAllBytes(Path.Combine(folder,name+".png"),tex.EncodeToPNG()); RenderTexture.active=null; cam.targetTexture=null;
            Destroy(rt); Destroy(tex);
        }
        void Call(object value,string method,params object[] args)
        { (value.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic) ?? typeof(HeroBase).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)).Invoke(value,args); }
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject); folder="Captures/audio_20261011"; Directory.CreateDirectory(folder); Application.logMessageReceived+=Log;
            for(int i=0;i<3;i++) { had[i]=PlayerPrefs.HasKey("BT.Audio."+settings[i]); old[i]=PlayerPrefs.GetFloat("BT.Audio."+settings[i]); GameAudio.SetVolume(settings[i],1,false); }
            yield return Wait(3f);
            var bus=GameAudio.I;
            bus.PlayedEvent+=events.Add;
            bus.SendMessage("OnApplicationFocus",true); yield return Wait(1f);
            Debug.Log("AUDIO_DIAGNOSTIC dsp="+AudioSettings.dspTime+" state="+bus.MusicState+" sources="+string.Join(";",Sources.Where(s=>s.priority==200).Select(s=>s.clip?.name+" time="+s.time+" playing="+s.isPlaying+" vol="+s.volume+" mute="+s.mute)));
            var clips=Resources.LoadAll<AudioClip>("SFX"); Check("all_34_sfx_load",clips.Length==34);
            foreach(var c in clips)
            {
                var d=new float[c.samples*c.channels]; bool read=c.GetData(d,0);
                Check("sample_"+c.name,read && c.channels==1 && c.frequency==44100 && c.length>.015f && d.Any(v=>Mathf.Abs(v)>.005f) && d.All(v=>Mathf.Abs(v)<.83f));
            }
            foreach(string n in new[]{"explore","combat","boss"})
            {
                var c=Resources.Load<AudioClip>("Audio/Music/"+n);
                Check("music_stream_"+n,c && c.channels==2 && c.length>60f && c.loadType==AudioClipLoadType.Streaming);
            }
            Check("menu_music_plays",bus.MusicState=="explore" && Sources.Any(s=>s.priority==200 && s.isPlaying && s.time>1f && s.volume>0));
            Check("menu_volume_controls",FindObjectOfType<AudioControls>() && FindObjectOfType<AudioControls>().GetComponentsInChildren<Button>().Length==3);
            float before=GameAudio.Effects; FindObjectOfType<AudioControls>().GetComponentsInChildren<Button>()[2].onClick.Invoke();
            Check("volume_button_changes_setting",!Mathf.Approximately(before,GameAudio.Effects)); GameAudio.SetVolume("Effects",1,false);
            GameAudio.SetVolume("Effects",0,false); yield return null; ClearVoices();
            Check("muted_effect_not_started",!bus.Play("hit_heavy") && bus.ActiveVoices==0);
            Check("weather_obeys_effect_mute",GameAudio.AmbientGain==0);
            Check("effect_mute_preserves_music",Sources.Any(s=>s.priority==200 && s.isPlaying && s.volume>0));
            GameAudio.SetVolume("Effects",1,false); yield return Wait(.15f);
            int played=bus.Played,dropped=bus.Dropped;
            for(int i=0;i<100;i++) bus.Play("enemy_death",.8f);
            Check("crowd_burst_capped",bus.Played-played<=1 && bus.Dropped-dropped>=99);
            Check("hurt_priority_survives_crowd",bus.Play("player_hurt",.9f) && Sources.Any(s=>s.isPlaying && s.clip.name=="player_hurt" && s.priority==16));
            yield return Wait(.2f);
            bus.Loop("chant_loop",.5f); yield return null;
            Check("chant_has_own_voice",Sources.Any(s=>s.loop && s.isPlaying && s.clip.name=="chant_loop")); bus.StopLoop();
            Check("chant_stop",!Sources.Any(s=>s.loop && s.isPlaying));
            var singleton=bus;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/BlackTide/Scenes/BlackTide_Slice.unity",new LoadSceneParameters(LoadSceneMode.Single)); yield return Wait(.75f);
            Check("audio_survives_scene_once",GameAudio.I==singleton && FindObjectsOfType<GameAudio>().Length==1);
            var hero=Combat.player; Check("slice_player_exists",hero!=null);
            if(!hero) { AudioProbe.Finish(false,"no hero"); yield break; }
            HeroBase.Autopilot=false; HeroBase.God=true; Feel.SetBaseScale(1);
            if(SliceGame.I) SliceGame.I.enabled=false;
            var boss=Combat.bosses.FirstOrDefault();
            foreach(var h in Combat.heroes) if(h && h!=hero) h.enabled=false;
            foreach(var e in Combat.enemies.ToArray()) if(e) e.gameObject.SetActive(false);
            Combat.enemies.Clear(); Combat.bosses.Clear(); Combat.threats.Clear();
            foreach(var canvas in FindObjectsOfType<Canvas>()) canvas.gameObject.SetActive(false);
            var go=new GameObject("AudioHitTarget"); var target=go.AddComponent<Damageable>(); target.team=Team.Enemy;
            target.maxHp=target.hp=100000; target.radius=.8f; target.height=2; target.staggerMax=100000;
            target.transform.position=hero.transform.position+Vector3.forward*2.5f; Combat.enemies.Add(target);
            Check("actual_enemy_selects_combat",bus.Context()=="combat");
            yield return Wait(3f); Check("combat_stream_switch",bus.MusicState=="combat" && Sources.Any(s=>s.isPlaying && s.clip.name=="combat" && s.volume>0));
            float hp=target.hp; played=bus.Played; ClearVoices(); events.Clear(); hero.RefreshCombatTarget();
            Call(hero,"StartBasic",false); yield return Wait(1.3f);
            Check("real_attack_hits",target.hp<hp);
            Check("real_attack_emits_sounds",events.Any(n=>n.StartsWith("swing_")) && events.Any(n=>n.StartsWith("hit_")));
            target.dead=true; Call(hero,"Enter",HeroBase.State.Move); yield return Wait(.2f); played=bus.Played; ClearVoices(); events.Clear();
            Call(hero,"StartBasic",false); yield return Wait(1.2f);
            Check("empty_swing_not_hit_sound",events.Any(n=>n.StartsWith("swing_")) && !events.Any(n=>n.StartsWith("hit_")));
            Check("distant_death_inaudible",!bus.PlayAt("enemy_death",hero.transform.position+Vector3.forward*35,.8f));
            Check("nearby_death_pans",bus.PlayAt("enemy_death",hero.transform.position+Camera.main.transform.right*8,.8f) && Sources.Any(s=>s.isPlaying && s.clip.name=="enemy_death" && s.panStereo>.5f));
            if(boss)
            {
                boss.enabled=false; boss.gameObject.SetActive(true); boss.dmg.dead=false; boss.dmg.dormant=false; boss.transform.position=hero.transform.position+Vector3.right*8;
                Combat.bosses.Add(boss); Check("nearby_boss_selects_boss",bus.Context()=="boss");
                yield return Wait(3f); Check("boss_stream_switch",bus.MusicState=="boss" && Sources.Any(s=>s.isPlaying && s.clip.name=="boss" && s.volume>0));
                boss.gameObject.SetActive(false); Combat.bosses.Clear();
            }
            yield return Wait(6.2f); Check("combat_hold_expires_to_explore",bus.Context()=="explore");
            yield return Wait(3f); Check("explore_stream_returns",bus.MusicState=="explore");
            // Fast-forward close to the real end of a streaming clip, then observe overlap.
            var active=Sources.First(s=>s.priority==200 && s.isPlaying); active.time=active.clip.length-1f;
            yield return Wait(.5f); Check("loop_overlaps_two_streams",Sources.Count(s=>s.priority==200 && s.isPlaying)==2);
            yield return Wait(1.4f); Check("loop_restarts_without_gap",Sources.Any(s=>s.priority==200 && s.isPlaying && s.time<4f));
            // Pause/focus lifecycle uses mute per source and releases transient effects.
            bus.SendMessage("OnApplicationFocus",false); Check("unfocused_all_muted",Sources.All(s=>s.mute));
            Check("unfocused_weather_muted",GameAudio.AmbientGain==0);
            bus.SendMessage("OnApplicationFocus",true); Check("refocus_restores_sources",Sources.All(s=>!s.mute));
            var tutorial=new GameObject("AudioTutorialTest").AddComponent<TutorialUI>(); tutorial.Build();
            var panel=(GameObject)typeof(TutorialUI).GetField("panel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tutorial); panel.SetActive(true);
            sample=new PadSample { connected=true }; GameInput.SampleOverride=()=>sample; GameInput.Reset(); yield return null;
            sample=new PadSample { connected=true,left=Vector2.right }; yield return null; sample=new PadSample { connected=true }; yield return null;
            float master=GameAudio.Master; sample=new PadSample { connected=true,buttons=PadButton.A }; yield return null; yield return null;
            Check("pad_volume_button_operates",!Mathf.Approximately(master,GameAudio.Master) || GameAudio.Music!=1 || GameAudio.Effects!=1);
            sample=default; GameInput.SampleOverride=null; GameInput.Reset(); panel.SetActive(false);
            Check("voice_budget_never_exceeds_pool",bus.ActiveVoices<=24);
            float peak=output.Count==0 ? 0 : output.Max(v=>Mathf.Abs(v));
            File.WriteAllText(Path.Combine(folder,"runtime-output.json"),"{\"meter_samples\":"+output.Count+",\"peak\":"+peak.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
            Check("listener_outputs_nonzero_audio",peak>.001f);
            Check("sampled_mix_has_headroom",peak<.98f);
            Check("no_audio_runtime_errors",errors==0);
            Restore();
            File.WriteAllText(Path.Combine(folder,"runtime-checks.txt"),string.Join("\n",checks));
            AudioProbe.Finish(failures.Count==0,"checks="+checks.Count+" failures="+string.Join(",",failures)+" outputPeak="+peak);
        }
        void Restore()
        {
            for(int i=0;i<3;i++) if(had[i]) PlayerPrefs.SetFloat("BT.Audio."+settings[i],old[i]); else PlayerPrefs.DeleteKey("BT.Audio."+settings[i]);
            PlayerPrefs.Save();
        }
        void OnDestroy() { Application.logMessageReceived-=Log; Restore(); }
    }
}
