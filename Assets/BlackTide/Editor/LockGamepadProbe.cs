using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BlackTide.EditorTools
{
    public static class LockGamepadProbe
    {
        const string Key="BlackTide.LockPadProbe";
        public static void Run()
        {
            SessionState.SetBool(Key,true); SessionState.SetBool(Key+".done",false);
            SessionState.SetFloat(Key+".start",(float)EditorApplication.timeSinceStartup);
            EditorSceneManager.OpenScene("Assets/BlackTide/Scenes/BlackTide_Slice.unity");
            Attach(); EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Attach() { EditorApplication.update-=Poll; EditorApplication.update+=Poll; }
        static void Poll()
        {
            if (!SessionState.GetBool(Key,false)) return;
            if (SessionState.GetBool(Key+".done",false) && !EditorApplication.isPlaying) { SessionState.SetBool(Key,false); EditorApplication.Exit(SessionState.GetInt(Key+".exit",1)); return; }
            if (EditorApplication.isPlaying && Combat.player && !UnityEngine.Object.FindObjectOfType<LockPadDriver>()) new GameObject("LockPadProbe").AddComponent<LockPadDriver>();
            if (EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".start",0)>180) Finish(false,"timeout");
        }
        public static void Finish(bool pass,string detail)
        {
            Debug.Log("LOCK_PAD_PROBE RESULT="+(pass ? "PASS" : "FAIL")+" "+detail);
            SessionState.SetBool(Key+".done",true); SessionState.SetInt(Key+".exit",pass ? 0 : 1); EditorApplication.ExitPlaymode();
        }
    }

    [DefaultExecutionOrder(-1000)] public class LockPadDriver : MonoBehaviour
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        HeroBase hero;
        Damageable target, other;
        PadSample sample;
        readonly List<string> checks=new List<string>(), failures=new List<string>();
        int errors;
        string folder;
        Camera camera;
        RenderTexture rt;
        Texture2D image;
        object Field(object o,string name) => o.GetType().GetField(name,Flags)?.GetValue(o) ?? typeof(HeroBase).GetField(name,Flags)?.GetValue(o);
        void Call(object o,string name,params object[] args) => (o.GetType().GetMethod(name,Flags) ?? typeof(HeroBase).GetMethod(name,Flags))?.Invoke(o,args);
        void Check(string name,bool ok) { checks.Add(name+"="+(ok ? "PASS" : "FAIL")); if (!ok) failures.Add(name); Debug.Log("LOCK_PAD_CHECK "+checks.Last()); }
        void Log(string m,string stack,LogType type) { if (type==LogType.Error || type==LogType.Exception || type==LogType.Assert) { errors++; if(type==LogType.Exception) LockGamepadProbe.Finish(false,m); } }
        PadSample Neutral() => new PadSample { connected=true };
        IEnumerator Frames(int n) { for(int i=0;i<n;i++) yield return null; }
        void SetHero(Vector3 targetPosition, WType? weapon=null)
        {
            hero.enabled=false;
            var cc=hero.GetComponent<CharacterController>(); cc.enabled=false;
            hero.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); cc.enabled=true;
            hero.kit.active=0;
            if (weapon.HasValue) { hero.kit.weapons[1]=Weapons.Make(Weapons.All.First(d=>d.type==weapon.Value),2); hero.kit.active=1; }
            hero.RefreshWeapon();
            target.transform.position=targetPosition; target.hp=target.maxHp=100000; target.dead=false; target.dormant=false;
            other.dead=true;
            hero.bufAttack=hero.bufDodge=hero.bufSkill=hero.bufUlt=hero.bufEcho=hero.bufWeapon=-9f;
            hero.attackHeld=false; hero.moveIn=Vector3.zero; hero.relayTarget=null; hero.walkHeld=false;
            Call(hero,"Enter",HeroBase.State.Move); Call(hero,"SetLockedTarget",(object)null);
            sample=Neutral(); GameInput.Reset(); hero.enabled=true;
        }
        Damageable MakeTarget(string name,Vector3 at)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Capsule); go.name=name; go.transform.position=at;
            var c=go.GetComponent<Collider>(); c.isTrigger=true;
            var d=go.AddComponent<Damageable>(); d.team=Team.Enemy; d.hp=d.maxHp=100000; d.radius=0.6f;
            Combat.enemies.Add(d); return d;
        }
        IEnumerator Start()
        {
            folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Captures","combat_pad_20261011")); Directory.CreateDirectory(folder);
            Application.logMessageReceived+=Log;
            Time.captureFramerate=60;
            hero=Combat.player;
            foreach (var h in Combat.heroes) h.enabled=false;
            foreach (var e in Combat.enemies.ToArray()) if(e) e.gameObject.SetActive(false);
            Combat.enemies.Clear(); Combat.threats.Clear(); HeroBase.Autopilot=false; HeroBase.SoftLock=true; HeroBase.God=true;
            if (SliceGame.I) SliceGame.I.enabled=false;
            foreach (var c in FindObjectsOfType<Canvas>()) c.gameObject.SetActive(false);
            target=MakeTarget("索敌测试目标",Vector3.back*7f); other=MakeTarget("备用目标",Vector3.right*4f); other.dead=true;
            camera=Camera.main; var rig=camera.GetComponent<CameraRig>(); if(rig) rig.enabled=false;
            camera.transform.position=new Vector3(9,14,-15); camera.transform.LookAt(new Vector3(0,0,-2));
            rt=new RenderTexture(640,480,24); image=new Texture2D(640,480,TextureFormat.RGB24,false);
            GameInput.Reset(); Check("native_abi_layout",System.Runtime.InteropServices.Marshal.SizeOf(typeof(GameInput).GetNestedType("NativeState",BindingFlags.NonPublic))==16);
            File.WriteAllText(Path.Combine(folder,"hardware.txt"),"XInput connected="+GameInput.Connected+"\nHardware feel not validated by injected samples.");
            var inputSettings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset")[0]);
            var axes=inputSettings.FindProperty("m_Axes"); bool noDuplicate=true;
            for(int i=0;i<axes.arraySize;i++) { var axis=axes.GetArrayElementAtIndex(i); string axisName=axis.FindPropertyRelative("m_Name").stringValue;
                if((axisName=="Horizontal" || axisName=="Vertical") && axis.FindPropertyRelative("type").intValue==2) noDuplicate=false;
                if((axisName=="Submit" || axisName=="Cancel") && axis.FindPropertyRelative("altPositiveButton").stringValue.Contains("joystick")) noDuplicate=false;
            }
            Check("legacy_input_no_duplicate_gamepad",noDuplicate);
            GameInput.SampleOverride=()=>sample; sample=Neutral(); GameInput.Reset(); yield return null;
            Check("radial_deadzone",GameInput.DeadZone(new Vector2(0.1f,0.1f))==Vector2.zero);
            Check("analog_rescale",GameInput.DeadZone(new Vector2(0.61f,0)).x>0.49f && GameInput.DeadZone(new Vector2(0.61f,0)).x<0.51f);
            var mappings=new[] { (PadButton.X,GameAction.Attack,false),(PadButton.B,GameAction.Dodge,false),(PadButton.A,GameAction.Interact,false),(PadButton.RB,GameAction.Skill,false),(PadButton.LB,GameAction.Echo,false),(PadButton.Y,GameAction.WeaponSkill,false),(PadButton.X,GameAction.SwitchHero,true),(PadButton.Y,GameAction.WeaponNext,true),(PadButton.LB,GameAction.Relay1,true),(PadButton.RB,GameAction.Relay2,true),(PadButton.LS,GameAction.Cards,false),(PadButton.Menu,GameAction.Guide,false),(PadButton.View,GameAction.Map,false),(PadButton.Up,GameAction.Item,false),(PadButton.Down,GameAction.TeamItem,false),(PadButton.Up,GameAction.Vessel,true) };
            foreach(var m in mappings)
            {
                sample=Neutral(); yield return null; GameInput.PadDown(PadButton.A);
                sample=new PadSample { connected=true,buttons=m.Item1,lt=m.Item3 ? 1f : 0f }; yield return null;
                Check("binding_"+m.Item2,GameInput.Down(m.Item2));
                if(m.Item3) Check("modifier_isolation_"+m.Item2,!GameInput.Down(m.Item1==PadButton.X ? GameAction.Attack : m.Item1==PadButton.Y ? GameAction.WeaponSkill : m.Item1==PadButton.LB ? GameAction.Echo : m.Item1==PadButton.RB ? GameAction.Skill : GameAction.Item));
                yield return null; Check("no_repeat_down_"+m.Item2,!GameInput.Down(m.Item2));
            }
            sample=Neutral(); yield return null; GameInput.PadDown(PadButton.A);
            sample=new PadSample { connected=true,rt=0.8f }; yield return null; Check("right_trigger_ult",GameInput.Down(GameAction.Ult));
            sample=Neutral(); yield return null; GameInput.PadDown(PadButton.A);
            sample=new PadSample { connected=true,buttons=PadButton.Menu,lt=1f }; yield return null; Check("restart_modifier_mapping",GameInput.Down(GameAction.Restart) && !GameInput.Down(GameAction.Guide));
            sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null; Check("attack_hold",GameInput.AttackHeld);
            sample=default; yield return null; Check("disconnect_releases",!GameInput.AttackHeld && GameInput.Move==Vector2.zero);
            sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null; Check("reconnect_no_phantom_press",!GameInput.Down(GameAction.Attack) && !GameInput.AttackHeld);
            yield return null; Check("reconnect_held_attack_suppressed",!GameInput.AttackHeld);
            sample=Neutral(); yield return null; GameInput.AttackHeld.ToString(); sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null;
            Check("reconnect_release_rearms",GameInput.Down(GameAction.Attack) && GameInput.AttackHeld);
            sample=default; yield return null; GameInput.AttackHeld.ToString(); sample=new PadSample { connected=true,rt=1f }; yield return null;
            Check("reconnect_trigger_suppressed",!GameInput.Down(GameAction.Ult));

            SetHero(Vector3.back*7f); yield return Frames(2);
            hero.AimPoint=Vector3.forward*100f; hero.RefreshCombatTarget(); Check("mouse_independent_behind_target",hero.LockedTarget==target && hero.AimPoint.z<0);
            sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null; sample=Neutral();
            yield return Frames(15); Shot("approach"); Check("approach_uses_move_state",hero.IsApproaching && hero.state==HeroBase.State.Move && hero.HorizontalVelocity.magnitude>1f);
            yield return Frames(95); Shot("landed"); Check("behind_target_chase_actual_hit",target.hp<100000 && hero.transform.position.z< -2f);
            Check("no_target_crossing",Vector3.Distance(hero.transform.position,target.transform.position)>=1f);
            hero.enabled=false; target.dead=true; other.dead=false; other.dormant=false; other.transform.position=hero.transform.position+Vector3.right*3f;
            hero.RefreshCombatTarget(); Check("dead_target_reacquires",hero.LockedTarget==other);
            other.dormant=true; hero.RefreshCombatTarget(); Check("dormant_target_excluded",hero.LockedTarget==null);
            other.dormant=false; other.gameObject.SetActive(false); hero.RefreshCombatTarget(); Check("inactive_target_excluded",hero.LockedTarget==null); other.gameObject.SetActive(true);
            Call(hero,"Enter",HeroBase.State.Move);
            target.dead=false; target.hp=800; target.transform.position=hero.transform.position+Vector3.back*3f;
            other.hp=400; other.transform.position=hero.transform.position+Vector3.right*4f;
            hero.RefreshCombatTarget(); Check("nearby_lowest_hp_priority",hero.LockedTarget==other);
            target.hp=100; hero.RefreshCombatTarget(); Check("health_change_reselects_without_cycle",hero.LockedTarget==target);
            other.hp=1; other.transform.position=hero.transform.position+Vector3.right*6f;
            hero.RefreshCombatTarget(); Check("distant_weak_enemy_does_not_override_near",hero.LockedTarget==target);
            other.hp=target.hp; other.transform.position=hero.transform.position+Vector3.right*2f;
            hero.RefreshCombatTarget(); Check("equal_hp_chooses_nearest",hero.LockedTarget==other);
            target.transform.position=hero.transform.position+Vector3.back*1.5f;
            hero.RefreshCombatTarget(); Check("closer_enemy_replaces_live_target",hero.LockedTarget==target);
            other.hp=1; Call(hero,"Enter",HeroBase.State.Attack); hero.RefreshCombatTarget();
            Check("single_swing_does_not_snap_to_another_enemy",hero.LockedTarget==target);
            Call(hero,"AttackTarget",6f,70f); Check("next_swing_reselects_weak_enemy",hero.LockedTarget==other);
            Call(hero,"Enter",HeroBase.State.Move);
            Call(hero,"SetLockedTarget",target); target.transform.position=hero.transform.position+Vector3.forward*30f; other.dead=true;
            hero.RefreshCombatTarget(); Check("out_of_range_release",!hero.LockedTarget);

            SetHero(Vector3.forward*8f); yield return Frames(2);
            sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null; sample=Neutral(); yield return Frames(12);
            Check("retarget_setup_is_actually_approaching",hero.IsApproaching);
            other.dead=false; other.hp=other.maxHp=500; other.transform.position=hero.transform.position+Vector3.left*2f;
            yield return Frames(65);
            Check("approach_retargets_new_nearby_enemy_and_hits",other.hp<500 && target.hp==100000);
            SetHero(Vector3.forward*2f); other.dead=false; other.hp=other.maxHp=500; other.transform.position=Vector3.left*2.8f;
            yield return Frames(2); sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null; sample=Neutral(); yield return Frames(55);
            Check("actual_attack_hits_nearby_lower_hp_enemy",other.hp<500);

            SetHero(Vector3.forward*8f); yield return Frames(2); sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null; sample=Neutral(); yield return Frames(12);
            sample=new PadSample { connected=true,buttons=PadButton.B,left=Vector2.right }; yield return Frames(2);
            Check("dodge_cancels_chase",hero.state==HeroBase.State.Dodge && !hero.IsApproaching); sample=Neutral(); yield return Frames(30);
            SetHero(Vector3.forward*8f); yield return Frames(2); sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null; sample=Neutral(); yield return Frames(12);
            sample=new PadSample { connected=true,left=Vector2.left }; yield return Frames(2); Check("movement_cancels_queued_chase",!hero.IsApproaching); sample=Neutral();

            foreach(var weapon in new[] { WType.重武器,WType.长柄,WType.双刃 })
            {
                SetHero(Vector3.back*6f,weapon); yield return Frames(2); sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null; sample=Neutral(); yield return Frames(130);
                Check("generic_chase_hit_"+weapon,target.hp<100000); Check("generic_no_crossing_"+weapon,Vector3.Distance(hero.transform.position,target.transform.position)>=1f);
            }
            SetHero(Vector3.forward*1.8f); int meleeHits=0; target.onHit=(hit,damage)=> { if(hit.kind=="melee" && damage>0) meleeHits++; };
            yield return Frames(2); sample=new PadSample { connected=true,buttons=PadButton.X };
            float comboStart=Time.time; int comboFrames=0;
            while(meleeHits<3 && Time.time-comboStart<4f && comboFrames++<360) yield return null;
            sample=Neutral();
            Debug.Log($"LOCK_PAD_COMBO hits={meleeHits} hp={target.hp} state={hero.state} clip={hero.anim.current} held={hero.attackHeld}");
            Check("hold_combo_hits",meleeHits>=3 && target.hp<100000); target.onHit=null;
            SetHero(Vector3.forward*2.4f); yield return Frames(2); sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null; sample=Neutral(); yield return Frames(2);
            target.transform.position=new Vector3(1.6f,0,2f); yield return Frames(30); Check("windup_tracks_moving_target",target.hp<100000);
            SetHero(Vector3.forward*7f); yield return Frames(2);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position=new Vector3(0,1,3); wall.transform.localScale=new Vector3(5,2,0.7f);
            sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null; sample=Neutral(); yield return Frames(150);
            Check("chase_respects_collision",hero.transform.position.z<2.3f && target.hp==100000);
            Check("stuck_chase_stops",!hero.IsApproaching); Destroy(wall); yield return null;
            hero.enabled=false; HeroBase.Autopilot=true; hero.AimPoint=new Vector3(8,0,3); hero.RefreshCombatTarget();
            Check("AI_aim_not_overridden",hero.AimPoint==new Vector3(8,0,3) && !hero.LockedTarget); HeroBase.Autopilot=false;
            var meleeHero=hero;
            foreach(var id in new[] { HeroId.Qinglan,HeroId.Chengyin })
            {
                hero=Combat.heroes.First(h=>h.Id==id); Combat.player=hero;
                SetHero(Vector3.back*6f); yield return Frames(2); sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null; sample=Neutral(); yield return Frames(80);
                Check("native_ranged_auto_hit_"+id,target.hp<100000 && !hero.IsApproaching); hero.enabled=false;
            }
            hero=meleeHero; Combat.player=hero;
            foreach(var weapon in new[] { WType.法杖,WType.双枪,WType.音感仪 })
            {
                SetHero(Vector3.back*6f,weapon); yield return Frames(2); sample=new PadSample { connected=true,buttons=PadButton.X }; yield return null; sample=Neutral(); yield return Frames(80);
                Check("generic_ranged_auto_hit_"+weapon,target.hp<100000 && !hero.IsApproaching);
            }
            SetHero(Vector3.forward*30f); yield return Frames(2); hero.enabled=false;

            var rogue=new GameObject("ProbeRogueUI").AddComponent<RogueUI>(); rogue.Build(); rogue.enabled=false; hero.rogue.pending=1;
            var panel=(GameObject)Field(rogue,"panel"); panel.SetActive(true); Call(rogue,"Update");
            sample=Neutral(); GameInput.Reset(); yield return Frames(2);
            sample=new PadSample { connected=true,left=Vector2.right }; yield return null; sample=Neutral(); yield return null;
            var nav=panel.GetComponent<GamepadUI>(); var selected=(Button)Field(nav,"selected"); Check("card_focus_navigation",selected && selected.transform.position.x>panel.transform.position.x-10);
            hero.enabled=true; sample=new PadSample { connected=true,buttons=PadButton.X }; yield return Frames(2); Check("modal_blocks_attack",hero.state==HeroBase.State.Move && !hero.attackHeld && hero.bufAttack<0); hero.enabled=false;
            sample=Neutral(); yield return null; sample=new PadSample { connected=true,buttons=PadButton.A }; yield return Frames(2); Check("card_A_commits",hero.rogue.pending==0); sample=Neutral(); yield return null;
            var shop=new GameObject("ProbeShopUI").AddComponent<ShopUI>(); shop.Build(); shop.enabled=false;
            hero.kit.coins=10000;
            var node=new NodeRuntime(new NodeDef("probe","测试商人",NodeType.Merchant,0,0,5,0)); node.shop=Economy.MakeShop("probe",false,new System.Random(1));
            Call(shop,"Open",node); yield return Frames(2);
            var shopPanel=(GameObject)Field(shop,"panel"); var shopNav=shopPanel.GetComponent<GamepadUI>(); var button=(Button)Field(shopNav,"selected");
            var beforeCoins=hero.kit.coins; sample=new PadSample { connected=true,buttons=PadButton.A }; yield return Frames(2);
            Check("shop_A_purchases",hero.kit.coins<beforeCoins); sample=Neutral(); yield return null;
            var scroll=shopPanel.GetComponentInChildren<ScrollRect>(); Check("shop_scroll_viewport",scroll && scroll.content.rect.height>=scroll.viewport.rect.height);
            sample=new PadSample { connected=true,buttons=PadButton.B }; yield return Frames(2); Check("shop_B_returns",!ShopUI.IsOpen); sample=Neutral(); yield return null;
            hero.enabled=true; yield return Frames(2); Check("no_action_leak_after_UI",hero.state==HeroBase.State.Move && !hero.attackHeld); hero.enabled=false;
            var guide=new GameObject("ProbeTutorialUI").AddComponent<TutorialUI>(); guide.Build();
            sample=Neutral(); yield return null; sample=new PadSample { connected=true,buttons=PadButton.Menu }; yield return Frames(2);
            Check("guide_Menu_opens",TutorialUI.IsOpen);
            var guideReader=(ScrollRect)Field(guide,"reader"); guideReader.verticalNormalizedPosition=1f;
            sample=new PadSample { connected=true,right=Vector2.down }; yield return Frames(20);
            Check("guide_stick_scrolls",guideReader.verticalNormalizedPosition<0.99f && guideReader.content.rect.height>=guideReader.viewport.rect.height);
            sample=new PadSample { connected=true,buttons=PadButton.B }; yield return Frames(2); Check("guide_B_returns",!TutorialUI.IsOpen);
            var menu=new GameObject("ProbeMainMenu").AddComponent<MainMenu>(); menu.enabled=false; Call(menu,"Build");
            sample=Neutral(); yield return Frames(2); sample=new PadSample { connected=true,left=Vector2.down }; yield return null; sample=Neutral(); yield return Frames(2);
            Call(menu,"Update");
            var menuNav=menu.GetComponentInChildren<GamepadUI>(); var menuButton=(Button)Field(menuNav,"selected");
            Check("main_menu_pad_navigation",menuButton && menuButton.GetComponentInChildren<Text>().text.Contains("教学"));
            sample=new PadSample { connected=true,buttons=PadButton.A }; yield return Frames(2);
            var help=(GameObject)Field(menu,"helpPanel"); Check("main_menu_A_opens_help",help.activeSelf);
            Canvas.ForceUpdateCanvases(); var canvasRect=(RectTransform)help.transform.parent;
            var helpBounds=RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRect,help.transform);
            Check("menu_help_within_canvas",canvasRect.rect.Contains(new Vector2(helpBounds.min.x,helpBounds.min.y)) && canvasRect.rect.Contains(new Vector2(helpBounds.max.x,helpBounds.max.y)));
            sample=Neutral(); yield return null; sample=new PadSample { connected=true,buttons=PadButton.B }; yield return null; Call(menu,"Update");
            Check("main_menu_B_returns",!help.activeSelf);
            sample=new PadSample { connected=true,left=Vector2.right }; yield return null;
            Call(menu,"Update"); hero.rogue.pending=1; panel.SetActive(true); Call(rogue,"Update");
            Check("buff_hint_pad",((Text)Field(rogue,"counter")).text.Contains("LS 按下") && ((Text)Field(rogue,"choiceHint")).text.Contains("A 确认") && !((Text[])Field(rogue,"cardText"))[0].text.Contains(">Z<"));
            panel.SetActive(false); Call(shop,"Update");
            Check("shop_hint_pad",((Text[])Field(shop,"slotText"))[GameInput.ItemSlot].text.Contains("↑ 使用") && ((Text)Field(shop,"closeHint")).text.Contains("B 返回"));
            var hud=FindObjectsOfType<HUD>(true).First(); Call(hud,"LateUpdate");
            Check("weapon_hint_pad",((Text)Field(hud,"weaponLabel")).text.Contains("LT + Y") && !((Text)Field(hud,"weaponLabel")).text.Contains("滚轮"));
            Check("no_lock_prompt",!((Text)Field(hud,"inputHint")).text.Contains("换目标") && ((Text)Field(hud,"inputHint")).text.Contains("残血优先"));
            Check("tutorial_pad_only",!TutorialUI.GuideSections().Contains("WASD") && !TutorialUI.GuideSections().Contains("强锁") && TutorialUI.GuideSections().Contains("LS 按下"));
            File.WriteAllText(Path.Combine(folder,"pad-hints.txt"),((Text)Field(hud,"weaponLabel")).text+"\n"+((Text)Field(rogue,"choiceHint")).text+"\n"+((Text)Field(rogue,"counter")).text+"\n"+TutorialUI.GuideSections());
            sample=default; yield return null; Call(hud,"LateUpdate"); Call(rogue,"Update");
            Check("disconnect_restores_keyboard_hints",!GameInput.UsingPad && ((Text)Field(hud,"weaponLabel")).text.Contains("滚轮") && ((Text)Field(rogue,"counter")).text.Contains(">B "));

            // 实际伤害结算 -> 双马达输出；测试接收器不会震动真实设备。
            sample=Neutral(); GameInput.Reset(); yield return null; sample=new PadSample { connected=true,left=Vector2.right }; yield return null;
            hero.enabled=false; target.dead=false; target.hp=target.maxHp=100000;
            float motorL=0,motorR=0; int motorCalls=0;
            var feedback=FindObjectOfType<GamepadFeedback>();
            Check("rumble_runtime_boot",feedback);
            Debug.Log($"RUMBLE_CONTEXT boot={feedback!=null} focused={(feedback ? Field(feedback,"focused") : null)} modal={GameInput.Modal} connected={GameInput.Connected} pad={GameInput.UsingPad} controlled={hero.IsControlled} brain={hero.UsesBrain}");
            if(feedback) Call(feedback,"OnApplicationFocus",true); // 无头窗口的失焦状态与样本输入一起模拟。
            GamepadFeedback.OutputOverride=(index,left,right)=> { motorL=left; motorR=right; motorCalls++; };
            GamepadFeedback.Stop();
            float dealt=hero.DealHit(target,0.1f,0,0,Vector3.forward,false,"melee",0,false);
            Check("actual_melee_hit_rumbles",dealt>0 && motorL>0 && motorR>motorL);
            float smallL=motorL,smallR=motorR; GamepadFeedback.Stop();
            hero.DealHit(target,0.1f,0,0,Vector3.forward,true,"melee",0,false);
            Check("heavy_hit_stronger",motorL>smallL && motorR>smallR);
            bool started=motorL>0; float scaled=Time.timeScale; Time.timeScale=0;
            yield return new WaitForSecondsRealtime(0.18f);
            Check("rumble_expires_during_hitstop",started && motorL==0 && motorR==0); Time.timeScale=scaled;
            int callsBefore=motorCalls; target.dead=true; hero.DealHit(target,1,0,0,Vector3.forward,true,"melee"); target.dead=false;
            hero.DealHit(target,0.1f,0,0,Vector3.forward,true,"burn");
            Check("miss_and_passive_damage_no_rumble",motorCalls==callsBefore);
            var ally=Combat.heroes.First(h=>h!=hero); GamepadFeedback.Hit(ally,true,"melee");
            HeroBase.Autopilot=true; GamepadFeedback.Hit(hero,true,"melee"); HeroBase.Autopilot=false;
            Check("ally_and_autopilot_no_rumble",motorCalls==callsBefore);
            HeroBase.God=false; typeof(HeroBase).GetField("invulnUntil",Flags).SetValue(hero,0f); hero.echoParryUntil=0;
            hero.dmg.dead=false; hero.dmg.hp=hero.dmg.maxHp; hero.dmg.shield=0; hero.dmg.guardUntil=0;
            float taken=hero.dmg.TakeHit(new HitInfo { damage=1,dir=Vector3.forward,kind="probe",heavy=true });
            Debug.Log($"RUMBLE_HURT taken={taken} accepted={hero.dmg.lastHitAccepted} low={motorL} high={motorR} state={hero.state}");
            Check("actual_hurt_low_frequency_rumble",taken>0 && motorL>motorR); HeroBase.God=true;
            GamepadFeedback.Stop(); hero.counterUntil=0; hero.PerfectDodge();
            Check("perfect_dodge_high_frequency_rumble",motorR>0.6f && motorL<0.2f);
            started=motorR>0; panel.SetActive(true); yield return null; Check("modal_stops_rumble",started && motorL==0 && motorR==0); panel.SetActive(false);
            GamepadFeedback.Hit(hero,true,"melee"); started=motorL>0; sample=default; yield return Frames(2); Check("disconnect_stops_rumble",started && motorL==0 && motorR==0);
            sample=Neutral(); yield return null; sample=new PadSample { connected=true,left=Vector2.right }; yield return null;
            GamepadFeedback.Hit(hero,true,"melee"); started=motorL>0; Call(feedback,"OnApplicationFocus",false);
            Check("focus_loss_stops_rumble",started && motorL==0 && motorR==0); callsBefore=motorCalls;
            GamepadFeedback.Hit(hero,true,"melee"); Check("unfocused_hit_cannot_restart_rumble",motorCalls==callsBefore); Call(feedback,"OnApplicationFocus",true);
            GamepadFeedback.Hit(hero,true,"melee"); started=motorL>0; Call(feedback,"OnApplicationPause",true); Check("pause_stops_rumble",started && motorL==0 && motorR==0); Call(feedback,"OnApplicationPause",false);
            GamepadFeedback.Hit(hero,true,"melee"); started=motorL>0; Call(feedback,"OnApplicationQuit"); Check("quit_stops_rumble",started && motorL==0 && motorR==0);
            Check("vibration_abi_layout",System.Runtime.InteropServices.Marshal.SizeOf(typeof(GamepadFeedback).GetNestedType("Vibration",BindingFlags.NonPublic))==4);
            GamepadFeedback.Stop(); GamepadFeedback.OutputOverride=null;
            Check("runtime_errors_zero",errors==0);
            GameInput.SampleOverride=null; Time.captureFramerate=0;
            File.WriteAllLines(Path.Combine(folder,"checks.txt"),checks);
            LockGamepadProbe.Finish(failures.Count==0 && errors==0,$"checks={checks.Count} failed={string.Join(",",failures)} errors={errors}");
        }
        void Shot(string name)
        {
            var old=camera.targetTexture; camera.targetTexture=rt; camera.Render(); var active=RenderTexture.active; RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,640,480),0,0); image.Apply(); File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());
            camera.targetTexture=old; RenderTexture.active=active;
        }
        void OnDestroy() { Application.logMessageReceived-=Log; GameInput.SampleOverride=null; Time.captureFramerate=0; }
    }
}
