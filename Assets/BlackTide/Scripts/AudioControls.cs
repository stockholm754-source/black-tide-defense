using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlackTide
{
    // Buttons work with the existing Xbox navigator; three channels persist across launches.
    public sealed class AudioControls : MonoBehaviour
    {
        readonly string[] channels={"Master","Music","Effects"};
        readonly string[] names={"总音量","音乐","音效"};
        readonly Text[] labels=new Text[3];
        public static GameObject Create(Transform parent,Font font,Vector2 anchor,Vector2 position,bool vertical,Func<bool> available=null)
        {
            var go=new GameObject("AudioControls",typeof(RectTransform),typeof(AudioControls));
            var rt=go.GetComponent<RectTransform>(); rt.SetParent(parent,false); rt.anchorMin=rt.anchorMax=anchor;
            rt.anchoredPosition=position; rt.sizeDelta=vertical ? new Vector2(330,225) : new Vector2(1000,50);
            var controls=go.GetComponent<AudioControls>();
            for(int i=0;i<3;i++)
            {
                int index=i;
                var bgo=new GameObject("Volume_"+controls.channels[i],typeof(RectTransform),typeof(Image),typeof(Button));
                var brt=bgo.GetComponent<RectTransform>(); brt.SetParent(rt,false);
                brt.sizeDelta=new Vector2(310,50); brt.anchoredPosition=vertical ? new Vector2(0,70-i*70) : new Vector2((i-1)*330,0);
                bgo.GetComponent<Image>().color=new Color(.07f,.14f,.17f,.98f);
                var button=bgo.GetComponent<Button>(); button.onClick.AddListener(()=>controls.Cycle(index));
                var tgo=new GameObject("Label",typeof(RectTransform),typeof(Text)); var trt=tgo.GetComponent<RectTransform>(); trt.SetParent(brt,false);
                trt.anchorMin=Vector2.zero; trt.anchorMax=Vector2.one; trt.offsetMin=trt.offsetMax=Vector2.zero;
                var t=tgo.GetComponent<Text>(); t.font=font; t.fontSize=21; t.alignment=TextAnchor.MiddleCenter; t.raycastTarget=false; t.color=new Color(.8f,.9f,.92f);
                controls.labels[i]=t;
            }
            if(available!=null) GamepadUI.Attach(go,available,null,!vertical);
            controls.Refresh(); return go;
        }
        void Cycle(int index)
        {
            float old=PlayerPrefs.GetFloat("BT.Audio."+channels[index],index==0 ? .8f : index==1 ? .65f : .9f);
            float value=old>=.99f ? 0 : Mathf.Min(1,(Mathf.Floor(old*5f+.01f)+1)*.2f);
            GameAudio.SetVolume(channels[index],value); GameAudio.Sfx("ui_confirm",.6f,0);
            Refresh();
        }
        void Refresh()
        {
            float[] values={GameAudio.Master,GameAudio.Music,GameAudio.Effects};
            for(int i=0;i<3;i++) if(labels[i]) labels[i].text=names[i]+"  "+Mathf.RoundToInt(values[i]*100)+"%  ↻";
        }
        void OnEnable() { Refresh(); }
    }

    // Runtime-made buttons also receive mouse/keyboard cues. Pad selection is hooked in GamepadUI.
    public sealed class UIAudioCue : MonoBehaviour,IPointerEnterHandler,ISelectHandler
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if(!FindObjectOfType<UIAudioScanner>()) DontDestroyOnLoad(new GameObject("UIAudioScanner",typeof(UIAudioScanner))); }
        void Awake() { GetComponent<Button>().onClick.AddListener(Confirm); }
        void Confirm() { GameAudio.Sfx("ui_confirm",.55f,0); }
        public void OnPointerEnter(PointerEventData data) { if(GetComponent<Button>().IsInteractable()) GameAudio.Sfx("ui_move",.45f,0); }
        public void OnSelect(BaseEventData data) { GameAudio.Sfx("ui_move",.45f,0); }
    }
    public sealed class UIAudioScanner : MonoBehaviour
    {
        float next;
        void Update()
        {
            if(Time.unscaledTime<next) return; next=Time.unscaledTime+.6f;
            foreach(var b in FindObjectsOfType<Button>()) if(!b.GetComponent<UIAudioCue>()) b.gameObject.AddComponent<UIAudioCue>();
        }
    }
}
