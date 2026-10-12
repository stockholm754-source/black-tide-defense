using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace BlackTide
{
    // 事件脉冲走非缩放时间；群体命中取最大强度，不逐个延长马达运转。
    public sealed class GamepadFeedback : MonoBehaviour
    {
        struct Pulse { public float end, duration, left, right; }
        static readonly Pulse[] pulses=new Pulse[4];
        static GamepadFeedback instance;
        static int sentDevice=-1;
        static float sentLeft, sentRight;
        static bool nativeMissing;
        bool focused=true, paused;
        public static Action<int,float,float> OutputOverride; // 回归接收器不驱动实体马达。
        [StructLayout(LayoutKind.Sequential)] struct Vibration { public ushort left,right; }
        [DllImport("xinput1_4.dll",EntryPoint="XInputSetState")] static extern uint SetState(uint index,ref Vibration vibration);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetState()
        {
            Stop(); instance=null; OutputOverride=null; nativeMissing=false;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] static void Boot()
        {
            if(instance) return;
            var go=new GameObject("GamepadFeedback"); instance=go.AddComponent<GamepadFeedback>(); DontDestroyOnLoad(go);
        }
        static bool Ready => instance && instance.focused && !instance.paused && (Application.isFocused || GameInput.SampleOverride!=null) && GameInput.Connected && GameInput.UsingPad && !GameInput.Modal;
        static void Add(int channel,float left,float right,float duration)
        {
            if(!Ready) return;
            float now=Time.unscaledTime;
            if(pulses[channel].end>now) return;
            pulses[channel]=new Pulse { end=now+duration,duration=duration,left=left,right=right };
            instance.Tick();
        }
        static bool Player(HeroBase h) => h && h.IsControlled && !h.UsesBrain;
        public static void Hit(HeroBase h,bool heavy,string kind)
        {
            if(!Player(h)) return;
            switch(kind)
            {
                case "melee": case "basic": case "heavy": case "shot": case "wave": case "skill": case "ult": case "wskill": case "echo":
                    Add(heavy ? 1 : 0,heavy ? 0.42f : 0.12f,heavy ? 0.55f : 0.32f,heavy ? 0.12f : 0.065f); break;
            }
        }
        public static void Hurt(HeroBase h,bool heavy) { if(Player(h)) Add(2,heavy ? 0.7f : 0.5f,0.22f,heavy ? 0.22f : 0.16f); }
        public static void Perfect(HeroBase h) { if(Player(h)) Add(3,0.18f,0.65f,0.13f); }
        void LateUpdate() { Tick(); }
        void Tick()
        {
            if(!Ready) { Stop(); return; }
            int index=OutputOverride!=null ? 0 : GameInput.ActiveDevice;
            if(index<0) { Stop(); return; }
            if(sentDevice>=0 && sentDevice!=index) Stop();
            float left=0,right=0,now=Time.unscaledTime;
            foreach(var p in pulses)
            {
                float gain=Mathf.Clamp01((p.end-now)/Mathf.Max(0.01f,p.duration)*3f);
                left=Mathf.Max(left,p.left*gain); right=Mathf.Max(right,p.right*gain);
            }
            if(sentDevice==index && Mathf.Abs(left-sentLeft)<0.002f && Mathf.Abs(right-sentRight)<0.002f) return;
            if(left<=0 && right<=0) { Stop(); return; }
            if(Write(index,left,right)) { sentDevice=index; sentLeft=left; sentRight=right; }
            else Stop();
        }
        static bool Write(int index,float left,float right)
        {
            if(OutputOverride!=null) { OutputOverride(index,left,right); return true; }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if(!nativeMissing && index>=0)
            {
                try { var v=new Vibration { left=(ushort)Mathf.RoundToInt(Mathf.Clamp01(left)*65535f),right=(ushort)Mathf.RoundToInt(Mathf.Clamp01(right)*65535f) }; return SetState((uint)index,ref v)==0; }
                catch(DllNotFoundException) { nativeMissing=true; }
                catch(EntryPointNotFoundException) { nativeMissing=true; }
            }
#endif
            return false;
        }
        public static void Stop()
        {
            Array.Clear(pulses,0,pulses.Length);
            if(sentDevice>=0) Write(sentDevice,0,0);
            sentDevice=-1; sentLeft=sentRight=0;
        }
        void OnApplicationFocus(bool value) { focused=value; if(!value) Stop(); }
        void OnApplicationPause(bool value) { paused=value; if(value) Stop(); }
        void OnApplicationQuit() { Stop(); }
        void OnDisable() { Stop(); }
        void OnDestroy() { Stop(); if(instance==this) instance=null; }
    }
}
