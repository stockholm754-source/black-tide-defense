using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace BlackTide
{
    [Flags] public enum PadButton : ushort
    {
        Up=1, Down=2, Left=4, Right=8, Menu=16, View=32, LS=64, RS=128,
        LB=256, RB=512, A=4096, B=8192, X=16384, Y=32768
    }
    public enum GameAction { Attack, Dodge, Skill, Ult, Echo, WeaponSkill, WeaponNext, Interact, SwitchHero, Relay1, Relay2, Cards, Guide, Map, Item, TeamItem, Vessel, Restart }

    // 按帧采样；原始设备与动作映射分开，编辑器可用同一采样路径做回归。
    public struct PadSample
    {
        public bool connected;
        public PadButton buttons;
        public Vector2 left, right;
        public float lt, rt;
    }

    public static class GameInput
    {
        static PadSample current, previous;
        static int frame=-1, blockedFrame=-1, device=-1;
        static float nextScan;
        static bool nativeMissing;
        static PadButton reconnectButtons;
        static bool reconnectTrigger;
        static bool usingPad;
        public static bool UsingPad { get { Poll(); return usingPad; } }
        public static string Hint(string keyboard,string pad) => UsingPad ? pad : keyboard;
        public static string ControlText(string text) => UsingPad ? text.Replace("（E）","（RB）").Replace("（Q）","（RT）").Replace("（R）","（LB）").Replace("（V）","（Y）") : text;
        internal static int ActiveDevice { get { Poll(); return SampleOverride!=null ? -1 : device; } }
        public static int ItemSlot { get; private set; }
        public static Func<PadSample> SampleOverride; // 仅显式测试赋值，正常游戏读取 XInput。
        public static bool Connected { get { Poll(); return current.connected; } }
        public static Vector2 Move { get { Poll(); var k=new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical")); return k.sqrMagnitude>0.01f ? Vector2.ClampMagnitude(k,1f) : current.left; } }
        public static Vector2 RightStick { get { Poll(); return current.right; } }
        public static bool Modifier { get { Poll(); return current.lt>0.4f; } }
        public static bool Modal => ShopUI.IsOpen || RogueUI.IsOpen || TutorialUI.IsOpen || ConsoleUI.IsOpen || MapUI.IsOpen;
        public static bool CombatBlocked { get { Poll(); return Modal || blockedFrame==Time.frameCount; } }
        public static void BlockCombatFrame() { blockedFrame=Time.frameCount; }
        public static Vector2 DeadZone(Vector2 value, float dead=0.22f)
        {
            float length=value.magnitude;
            return length<=dead ? Vector2.zero : value/length * Mathf.Clamp01((length-dead)/(1f-dead));
        }
        public static void Reset()
        {
            current=previous=default; frame=blockedFrame=device=-1; nextScan=0f; usingPad=false; ItemSlot=0;
            reconnectButtons=0; reconnectTrigger=false;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Init() { SampleOverride=null; Reset(); }
        static void Poll()
        {
            if (frame==Time.frameCount) return;
            frame=Time.frameCount;
            previous=current;
            current=SampleOverride!=null ? SampleOverride() : ReadNative();
            if (!Application.isFocused && SampleOverride==null) current=default;
            // 重连/恢复焦点时已按住的按钮和右扳机，必须先松开才重新参与动作。
            bool connecting=current.connected && !previous.connected;
            if (connecting) { reconnectButtons=current.buttons; reconnectTrigger=current.rt>0.35f; }
            if (!current.connected) { reconnectButtons=0; reconnectTrigger=false; }
            reconnectButtons &= current.buttons;
            current.buttons &= ~reconnectButtons;
            if (reconnectTrigger) { if(current.rt<=0.35f) reconnectTrigger=false; else current.rt=0f; }
            if (connecting) previous=current;
            if (Modal) BlockCombatFrame();
            if (Combat.player && (Pressed(PadButton.Menu) || Pressed(PadButton.LS) || Pressed(PadButton.View))) BlockCombatFrame();
            if (current.connected && (current.buttons!=previous.buttons || current.left.sqrMagnitude>0.01f || current.right.sqrMagnitude>0.01f || Mathf.Abs(current.rt-previous.rt)>0.1f || Mathf.Abs(current.lt-previous.lt)>0.1f)) usingPad=true;
            if (!current.connected) usingPad=false;
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Mathf.Abs(Input.mouseScrollDelta.y)>0.01f) usingPad=false;
            foreach (var key in KeyboardActions) if(Input.GetKeyDown(key)) { usingPad=false; break; }
            if (!Modal && !Modifier)
            {
                if (Pressed(PadButton.Left)) ItemSlot=(ItemSlot+Economy.Slots-1)%Economy.Slots;
                if (Pressed(PadButton.Right)) ItemSlot=(ItemSlot+1)%Economy.Slots;
            }
        }
        static bool Pressed(PadButton b) => current.connected && (current.buttons&b)!=0 && (previous.buttons&b)==0;
        static bool Held(PadButton b) => current.connected && (current.buttons&b)!=0;
        public static bool PadDown(PadButton b) { Poll(); return Pressed(b); }
        public static bool PadHeld(PadButton b) { Poll(); return Held(b); }
        public static bool AttackHeld { get { Poll(); return Input.GetMouseButton(0) || (!Modifier && Held(PadButton.X)); } }
        public static bool InteractHeld { get { Poll(); return Input.GetKey(KeyCode.F) || (!Modifier && Held(PadButton.A)); } }
        public static bool Down(GameAction a)
        {
            Poll();
            switch (a)
            {
                case GameAction.Attack: return Input.GetMouseButtonDown(0) || (!Modifier && Pressed(PadButton.X));
                case GameAction.Dodge: return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.LeftShift) || Input.GetMouseButtonDown(1) || (!Modifier && Pressed(PadButton.B));
                case GameAction.Skill: return Input.GetKeyDown(KeyCode.E) || (!Modifier && Pressed(PadButton.RB));
                case GameAction.Ult: return Input.GetKeyDown(KeyCode.Q) || (!Modifier && current.rt>0.35f && previous.rt<=0.35f);
                case GameAction.Echo: return Input.GetKeyDown(KeyCode.R) || (!Modifier && Pressed(PadButton.LB));
                case GameAction.WeaponSkill: return Input.GetKeyDown(KeyCode.V) || (!Modifier && Pressed(PadButton.Y));
                case GameAction.WeaponNext: return Input.GetKeyDown(KeyCode.X) || (Modifier && Pressed(PadButton.Y));
                case GameAction.Interact: return Input.GetKeyDown(KeyCode.F) || (!Modifier && Pressed(PadButton.A));
                case GameAction.SwitchHero: return Input.GetKeyDown(KeyCode.Tab) || (Modifier && Pressed(PadButton.X));
                case GameAction.Relay1: return Input.GetKeyDown(KeyCode.Alpha1) || (Modifier && Pressed(PadButton.LB));
                case GameAction.Relay2: return Input.GetKeyDown(KeyCode.Alpha2) || (Modifier && Pressed(PadButton.RB));
                case GameAction.Cards: return Input.GetKeyDown(KeyCode.B) || Pressed(PadButton.LS);
                case GameAction.Guide: return Input.GetKeyDown(KeyCode.H) || (!Modifier && Pressed(PadButton.Menu));
                case GameAction.Restart: return Input.GetKeyDown(KeyCode.F2) || (Modifier && Pressed(PadButton.Menu));
                case GameAction.Map: return Input.GetKeyDown(KeyCode.M) || Pressed(PadButton.View);
                case GameAction.Item: return !Modifier && Pressed(PadButton.Up);
                case GameAction.TeamItem: return Input.GetKeyDown(KeyCode.Alpha6) || (!Modifier && Pressed(PadButton.Down));
                case GameAction.Vessel: return Input.GetKeyDown(KeyCode.Alpha7) || (Modifier && Pressed(PadButton.Up));
                default: return false;
            }
        }
        static readonly KeyCode[] KeyboardActions={KeyCode.W,KeyCode.A,KeyCode.S,KeyCode.D,KeyCode.E,KeyCode.Q,KeyCode.R,KeyCode.V,KeyCode.F,KeyCode.G,KeyCode.B,KeyCode.H,KeyCode.M,KeyCode.X,KeyCode.Z,KeyCode.C,KeyCode.Tab,KeyCode.Space,KeyCode.LeftShift,KeyCode.LeftControl,KeyCode.Return,KeyCode.KeypadEnter,KeyCode.Escape,KeyCode.UpArrow,KeyCode.DownArrow,KeyCode.LeftArrow,KeyCode.RightArrow,KeyCode.Alpha1,KeyCode.Alpha2,KeyCode.Alpha3,KeyCode.Alpha4,KeyCode.Alpha5,KeyCode.Alpha6,KeyCode.Alpha7,KeyCode.F1,KeyCode.F2,KeyCode.F6,KeyCode.F7,KeyCode.BackQuote};
        [StructLayout(LayoutKind.Sequential)] struct NativePad { public ushort buttons; public byte lt,rt; public short lx,ly,rx,ry; }
        [StructLayout(LayoutKind.Sequential)] struct NativeState { public uint packet; public NativePad pad; }
        [DllImport("xinput1_4.dll", EntryPoint="XInputGetState")] static extern uint GetState(uint index, out NativeState state);
        static PadSample ReadNative()
        {
            if (nativeMissing) return default;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                NativeState s;
                if (device>=0 && GetState((uint)device,out s)==0) return Convert(s);
                device=-1;
                if (Time.unscaledTime<nextScan) return default;
                nextScan=Time.unscaledTime+0.5f;
                for (uint i=0;i<4;i++) if (GetState(i,out s)==0) { device=(int)i; return Convert(s); }
            }
            catch (DllNotFoundException) { nativeMissing=true; Debug.LogWarning("XInput 不可用，继续使用键鼠输入。"); }
            catch (EntryPointNotFoundException) { nativeMissing=true; Debug.LogWarning("XInput 接口不可用，继续使用键鼠输入。"); }
#endif
            return default;
        }
        static PadSample Convert(NativeState s) => new PadSample { connected=true, buttons=(PadButton)s.pad.buttons, lt=s.pad.lt/255f,rt=s.pad.rt/255f,
            left=DeadZone(new Vector2(s.pad.lx/32767f,s.pad.ly/32767f)),right=DeadZone(new Vector2(s.pad.rx/32767f,s.pad.ry/32767f),0.25f) };
    }
}
