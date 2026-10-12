using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlackTide
{
    // 同一导航器供菜单、选卡与商店使用；A 确认、B 返回，金色框表明焦点。
    public class GamepadUI : MonoBehaviour
    {
        Func<bool> available;
        Action cancel;
        bool horizontal;
        Button selected;
        Outline outline;
        float repeatAt;
        int previousDirection;
        int enabledFrame;
        readonly List<Button> buttons=new List<Button>();
        ScrollRect reader;
        public static ScrollRect MakeTextReader(GameObject panel, Text text, Func<bool> available)
        {
            var go=new GameObject("GuideViewport",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));
            var viewport=go.GetComponent<RectTransform>(); viewport.SetParent(panel.transform,false);
            viewport.anchorMin=Vector2.zero; viewport.anchorMax=Vector2.one;
            viewport.offsetMin=new Vector2(60f,35f); viewport.offsetMax=new Vector2(-60f,-95f);
            go.GetComponent<Image>().color=Color.clear;
            var content=text.rectTransform; content.SetParent(viewport,false);
            content.anchorMin=new Vector2(0f,1f); content.anchorMax=Vector2.one; content.pivot=new Vector2(0.5f,1f); content.anchoredPosition=Vector2.zero;
            var scroll=go.GetComponent<ScrollRect>(); scroll.viewport=viewport; scroll.content=content; scroll.horizontal=false;
            scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=45f;
            var nav=go.AddComponent<GamepadUI>(); nav.available=available; nav.reader=scroll;
            RefreshReader(scroll,text); return scroll;
        }
        public static void RefreshReader(ScrollRect scroll, Text text)
        {
            Canvas.ForceUpdateCanvases();
            scroll.content.sizeDelta=new Vector2(0f,Mathf.Max(scroll.viewport.rect.height,text.preferredHeight+16f));
        }
        public static void Attach(GameObject root, Func<bool> available, Action cancel, bool horizontal=false)
        {
            var ui=root.AddComponent<GamepadUI>(); ui.available=available; ui.cancel=cancel; ui.horizontal=horizontal;
        }
        void LateUpdate()
        {
            bool connected=GameInput.Connected;
            if (available==null || !available() || !GameInput.UsingPad || !connected) { ClearFocus(); return; }
            if (reader)
            {
                float scrollAxis=GameInput.RightStick.y;
                if (Mathf.Abs(scrollAxis)<0.1f) scrollAxis=GameInput.Move.y;
                if (GameInput.PadHeld(PadButton.Up)) scrollAxis=1f;
                if (GameInput.PadHeld(PadButton.Down)) scrollAxis=-1f;
                reader.verticalNormalizedPosition=Mathf.Clamp01(reader.verticalNormalizedPosition+scrollAxis*Time.unscaledDeltaTime*0.8f);
                return;
            }
            buttons.Clear();
            foreach (var b in GetComponentsInChildren<Button>()) if (b.IsInteractable() && b.gameObject.activeInHierarchy) buttons.Add(b);
            buttons.Sort((a,b)=> horizontal ? a.transform.position.x.CompareTo(b.transform.position.x) : b.transform.position.y.CompareTo(a.transform.position.y));
            if (buttons.Count==0) return;
            if (!selected || !buttons.Contains(selected)) Select(buttons[0]);
            Vector2 axis=GameInput.Move;
            int direction=horizontal ? (axis.x>0.55f ? 1 : axis.x< -0.55f ? -1 : 0) : (axis.y< -0.55f ? 1 : axis.y>0.55f ? -1 : 0);
            if (GameInput.PadHeld(horizontal ? PadButton.Right : PadButton.Down)) direction=1;
            if (GameInput.PadHeld(horizontal ? PadButton.Left : PadButton.Up)) direction=-1;
            if (direction!=0 && (direction!=previousDirection || Time.unscaledTime>=repeatAt))
            {
                Select(buttons[(buttons.IndexOf(selected)+direction+buttons.Count)%buttons.Count]);
                repeatAt=Time.unscaledTime+(direction!=previousDirection ? 0.3f : 0.14f);
            }
            previousDirection=direction;
            if (GameInput.PadDown(PadButton.B)) { GameInput.BlockCombatFrame(); cancel?.Invoke(); ClearFocus(); return; }
            if (Time.frameCount!=enabledFrame && GameInput.PadDown(PadButton.A) && selected) { GameInput.BlockCombatFrame(); selected.onClick.Invoke(); }
        }
        void Select(Button b)
        {
            ClearFocus(); selected=b;
            GameAudio.Sfx("ui_move",.45f,0);
            outline=b.GetComponent<Outline>(); if (!outline) outline=b.gameObject.AddComponent<Outline>();
            outline.effectColor=new Color(1f,0.83f,0.3f); outline.effectDistance=new Vector2(3f,-3f); outline.enabled=true;
            var scroll=b.GetComponentInParent<ScrollRect>();
            if (scroll && scroll.content && scroll.viewport)
            {
                Canvas.ForceUpdateCanvases();
                var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,b.transform);
                var rect=scroll.viewport.rect; var p=scroll.content.anchoredPosition;
                if (bounds.min.y<rect.yMin) p.y+=rect.yMin-bounds.min.y;
                else if (bounds.max.y>rect.yMax) p.y-=bounds.max.y-rect.yMax;
                scroll.content.anchoredPosition=p;
            }
        }
        void ClearFocus() { if (outline) outline.enabled=false; selected=null; outline=null; }
        void OnDisable() { ClearFocus(); }
        void OnEnable() { enabledFrame=Time.frameCount; previousDirection=0; }
    }
}
