using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlackTide
{
    // Persistent audio bus: UI survives scene changes; crowd voices cannot steal critical cues.
    public sealed class GameAudio : MonoBehaviour
    {
        public static GameAudio I { get; private set; }
        public static float Master => PlayerPrefs.GetFloat("BT.Audio.Master", .8f);
        public static float Music => PlayerPrefs.GetFloat("BT.Audio.Music", .65f);
        public static float Effects => PlayerPrefs.GetFloat("BT.Audio.Effects", .9f);
        public static float AmbientGain => I && (!I.focused || I.paused) ? 0 : Master*Effects;
        public string MusicState { get; private set; } = "";
        public string LastEvent { get; private set; }
        public int Played { get; private set; }
        public int Dropped { get; private set; }
        public event System.Action<string> PlayedEvent;
        public int ActiveVoices { get { int n=0; foreach(var v in voices) if(v.source.isPlaying) n++; return n; } }
        sealed class Voice { public AudioSource source; public float gain, started; public int importance; public string name; }
        readonly List<Voice> voices=new List<Voice>();
        readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        readonly Dictionary<string,float> last=new Dictionary<string,float>();
        readonly Dictionary<string,AudioClip> tracks=new Dictionary<string,AudioClip>();
        AudioSource chant;
        readonly AudioSource[] music=new AudioSource[2];
        float chantGain, combatUntil, nextContext, fadeStart, duckUntil, nextTransition;
        float fadeDuration=2.2f, outgoingStart, musicGain;
        int incoming, outgoing=1, frameBudget, budgetFrame=-1;
        bool fading, focused=true, paused;
        HeroBase stepHero;
        Voice lastVoice;
        Vector3 stepPosition;
        float stepDistance;
        int stepVariant;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { I=null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { Ensure(); }
        public static GameAudio Ensure()
        {
            if(!I) new GameObject("GameAudio").AddComponent<GameAudio>();
            return I;
        }
        void Awake()
        {
            if(I && I!=this) { Destroy(gameObject); return; }
            I=this; DontDestroyOnLoad(gameObject);
            for(int i=0;i<24;i++) voices.Add(new Voice { source=Source(128) });
            chant=Source(100); chant.loop=true;
            for(int i=0;i<2;i++) music[i]=Source(200);
            SceneManager.activeSceneChanged+=SceneChanged;
        }
        AudioSource Source(int priority)
        {
            var s=gameObject.AddComponent<AudioSource>(); s.playOnAwake=false; s.spatialBlend=0; s.priority=priority;
            return s;
        }
        void SceneChanged(Scene from,Scene to)
        {
            foreach(var v in voices) if(string.IsNullOrEmpty(v.name) || !v.name.StartsWith("ui_")) v.source.Stop();
            chant.Stop(); last.Clear(); combatUntil=0; nextContext=0; nextTransition=0;
        }
        public AudioClip Clip(string name)
        {
            if(!clips.TryGetValue(name,out var c))
            {
                c=Resources.Load<AudioClip>("SFX/"+name); clips[name]=c;
                if(!c) Debug.LogWarning("AUDIO_MISSING SFX/"+name);
            }
            return c;
        }
        public static void Sfx(string name,float gain=1,float variation=.06f,float pitch=1)
        { Ensure().Play(name,gain,variation,pitch); }
        public bool Play(string name,float gain=1,float variation=.06f,float pitch=1)
        {
            if(!focused || paused || gain<=0 || Master*Effects<=0) return false;
            var clip=Clip(name); if(!clip) return false;
            bool ui=name.StartsWith("ui_") || name=="purchase" || name=="buff_pick";
            bool critical=name=="attack_warning" || (gain>=.5f && (name=="player_hurt" || name=="perfect_dodge" || name.StartsWith("ult_") || name=="stagger_break"));
            int importance=ui ? 8 : critical ? 16 : gain<.5f || name=="enemy_death" ? 150 : 60;
            string family=name.StartsWith("hit_") ? "hit" : name.StartsWith("swing_") ? "swing" : name;
            if(importance==150) family="background/"+family;
            float interval=ui ? .075f : critical ? .09f : importance==150 ? .13f : .045f;
            float now=Time.unscaledTime;
            if(last.TryGetValue(family,out float t) && now-t<interval) { Dropped++; return false; }
            if(budgetFrame!=Time.frameCount) { budgetFrame=Time.frameCount; frameBudget=0; }
            if(!critical && !ui && frameBudget>=7) { Dropped++; return false; }
            int background=0,world=0;
            foreach(var v in voices) if(v.source.isPlaying) { if(v.importance>=150) background++; if(v.importance>=60) world++; }
            if((importance>=150 && background>=6) || (!critical && !ui && world>=14)) { Dropped++; return false; }
            Voice chosen=null;
            foreach(var v in voices) if(!v.source.isPlaying) { chosen=v; break; }
            if(chosen==null)
            {
                foreach(var v in voices) if(v.importance>importance && (chosen==null || v.importance>chosen.importance || (v.importance==chosen.importance && v.started<chosen.started))) chosen=v;
            }
            if(chosen==null) { Dropped++; return false; }
            last[family]=now; frameBudget++;
            chosen.source.Stop(); chosen.name=name; chosen.importance=importance; chosen.started=now;
            chosen.gain=Mathf.Clamp01(gain)*(importance==150 ? .55f : .8f);
            chosen.source.priority=importance;
            chosen.source.panStereo=0;
            chosen.source.pitch=Mathf.Clamp(pitch*(1+Random.Range(-variation,variation)),.5f,2.5f);
            chosen.source.clip=clip; chosen.source.volume=chosen.gain*Master*Effects; chosen.source.Play();
            Played++; LastEvent=name;
            lastVoice=chosen; PlayedEvent?.Invoke(name);
            if(critical) duckUntil=Mathf.Max(duckUntil,now+(name=="ult_hit" ? .75f : .32f));
            return true;
        }
        public bool PlayAt(string name,Vector3 position,float gain=1,float variation=.06f,float pitch=1)
        {
            if(!Combat.player) return Play(name,gain,variation,pitch);
            var offset=position-Combat.player.transform.position; offset.y=0;
            float distance=offset.magnitude;
            if(distance>=30) return false;
            float attenuation=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(4,30,distance));
            if(!Play(name,gain*attenuation,variation,pitch)) return false;
            var cam=Camera.main;
            if(cam) lastVoice.source.panStereo=Mathf.Clamp(Vector3.Dot(offset,cam.transform.right)/(distance+4),-.7f,.7f);
            return true;
        }
        public void Loop(string name,float gain)
        {
            if(string.IsNullOrEmpty(name)) { StopLoop(); return; }
            var c=Clip(name); if(!c) return;
            chantGain=Mathf.Clamp01(gain)*.55f;
            if(chant.clip!=c || !chant.isPlaying) { chant.clip=c; chant.Play(); }
            chant.volume=chantGain*Master*Effects;
        }
        public void StopLoop() { if(chant) chant.Stop(); }
        public static void SetVolume(string channel,float value,bool save=true)
        {
            if(channel!="Master" && channel!="Music" && channel!="Effects") return;
            PlayerPrefs.SetFloat("BT.Audio."+channel,Mathf.Clamp01(value)); if(save) PlayerPrefs.Save();
        }
        public string Context()
        {
            bool menu=SceneManager.GetActiveScene().name=="MainMenu";
            if(menu || !Combat.player) return "explore";
            var run=RunDirector.I;
            if(run && run.ended) return "explore";
            var p=Combat.player.transform.position;
            bool boss=run && run.finaleStarted;
            foreach(var b in Combat.bosses) if(b && b.dmg && !b.dmg.dead && !b.dmg.dormant && b.gameObject.activeInHierarchy && Vector3.Distance(b.transform.position,p)-b.dmg.radius<24f) { boss=true; break; }
            bool nearby=false;
            foreach(var e in Combat.enemies) if(e && !e.dead && !e.dormant && e.gameObject.activeInHierarchy && (e.transform.position-p).sqrMagnitude<20f*20f) { nearby=true; break; }
            if(nearby) combatUntil=Time.unscaledTime+6f;
            return boss ? "boss" : Time.unscaledTime<combatUntil ? "combat" : "explore";
        }
        public void Transition(string state,float seconds=2.2f)
        {
            if(!tracks.TryGetValue(state,out var c)) { c=Resources.Load<AudioClip>("Audio/Music/"+state); tracks[state]=c; }
            if(!c) { nextTransition=Time.unscaledTime+5f; Debug.LogWarning("AUDIO_MISSING music/"+state); return; }
            outgoing=incoming; incoming=1-incoming;
            outgoingStart=music[outgoing].volume;
            music[incoming].Stop(); music[incoming].clip=c; music[incoming].loop=false;
            music[incoming].volume=0; music[incoming].Play();
            fadeStart=Time.unscaledTime; fadeDuration=Mathf.Max(.1f,seconds); fading=true;
            MusicState=state; nextTransition=Time.unscaledTime+fadeDuration;
        }
        void Update()
        {
            float now=Time.unscaledTime, dt=Time.unscaledDeltaTime;
            if(now>=nextContext && now>=nextTransition)
            {
                nextContext=now+.35f; string desired=Context();
                if(desired!=MusicState || !music[incoming].isPlaying) Transition(desired);
                else if(music[incoming].clip.length-music[incoming].time<1.2f) Transition(desired,1.15f);
            }
            float target=Master*Music*.68f*(SceneManager.GetActiveScene().name=="MainMenu" ? .85f : 1f);
            if(GameInput.Modal) target*=.6f;
            if(now<duckUntil) target*=.42f;
            if(!focused || paused) target=0;
            musicGain=Mathf.MoveTowards(musicGain,target,dt*1.5f);
            Footsteps();
            if(fading)
            {
                float k=Mathf.Clamp01((now-fadeStart)/fadeDuration);
                music[incoming].volume=musicGain*Mathf.Sin(k*Mathf.PI*.5f);
                music[outgoing].volume=Mathf.Min(outgoingStart,musicGain)*Mathf.Cos(k*Mathf.PI*.5f);
                if(k>=1) { fading=false; music[outgoing].Stop(); }
            }
            else music[incoming].volume=musicGain;
            foreach(var v in voices) if(v.source.isPlaying) v.source.volume=v.gain*Master*Effects;
            chant.volume=chantGain*Master*Effects;
        }
        void Footsteps()
        {
            var h=Combat.player;
            if(!h) { stepHero=null; return; }
            Vector3 p=h.transform.position;
            if(stepHero!=h) { stepHero=h; stepPosition=p; stepDistance=0; return; }
            var delta=p-stepPosition; delta.y=0; stepPosition=p;
            if(h.state!=HeroBase.State.Move || GameInput.Modal || delta.magnitude>1.8f) { stepDistance=0; return; }
            stepDistance+=delta.magnitude;
            if(stepDistance>=1.65f && delta.sqrMagnitude>.00001f)
            {
                stepDistance%=1.65f; stepVariant=stepVariant%3+1;
                Play("footstep_"+stepVariant,.3f,.055f);
            }
        }
        void Mute(bool value)
        {
            foreach(var v in voices) { v.source.mute=value; if(value) v.source.Stop(); }
            chant.mute=value; foreach(var s in music) s.mute=value;
        }
        void OnApplicationFocus(bool value) { focused=value; Mute(!focused || paused); }
        void OnApplicationPause(bool value) { paused=value; Mute(!focused || paused); }
        void OnDestroy() { SceneManager.activeSceneChanged-=SceneChanged; if(I==this) I=null; }
    }
}
