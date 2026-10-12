using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace BlackTide
{
    // Neon belongs to local skill geometry. Camera grading stays stable across combat states.
    public class CyberGrade : MonoBehaviour
    {
        public static CyberGrade I;
        Volume vol;
        Canvas canvas;
        Text warning;
        public void Build()
        {
            I=this;
            if(vol) return;
            var cam=Camera.main;
            if(cam)
            {
                var data=cam.GetComponent<UniversalAdditionalCameraData>();
                if(!data) data=cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing=true;
            }
            var go=new GameObject("CyberGradeVolume"); go.transform.SetParent(transform,false);
            vol=go.AddComponent<Volume>(); vol.isGlobal=true; vol.priority=60; vol.weight=0;
            vol.sharedProfile=ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom=vol.sharedProfile.Add<Bloom>(true);
            bloom.threshold.Override(1.1f); bloom.intensity.Override(.65f); bloom.scatter.Override(.45f);
            bloom.tint.Override(Color.white); bloom.highQualityFiltering.Override(true);
            vol.sharedProfile.Add<ChromaticAberration>(true).intensity.Override(0);
            vol.sharedProfile.Add<FilmGrain>(true).intensity.Override(0);
            vol.sharedProfile.Add<LensDistortion>(true).intensity.Override(0);
            var vignette=vol.sharedProfile.Add<Vignette>(true);
            vignette.intensity.Override(.12f); vignette.smoothness.Override(.5f);
            vignette.color.Override(new Color(.01f,.02f,.035f));
            var colors=vol.sharedProfile.Add<ColorAdjustments>(true);
            colors.saturation.Override(3); colors.contrast.Override(3); colors.postExposure.Override(0);
            var ui=new GameObject("CyberCanvas",typeof(Canvas),typeof(CanvasScaler)); ui.transform.SetParent(transform,false);
            canvas=ui.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=70;
            var scaler=ui.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=.5f;
            var label=new GameObject("RealDanger",typeof(RectTransform),typeof(Text)); label.transform.SetParent(ui.transform,false);
            var rt=label.GetComponent<RectTransform>(); rt.anchorMin=rt.anchorMax=new Vector2(.5f,1);
            rt.pivot=new Vector2(.5f,1); rt.anchoredPosition=new Vector2(0,-115); rt.sizeDelta=new Vector2(680,36);
            warning=label.GetComponent<Text>();
            warning.font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei UI","SimHei","Arial"},22);
            warning.fontSize=22; warning.alignment=TextAnchor.MiddleCenter;
            warning.color=new Color(1,.65f,.25f); warning.raycastTarget=false;
        }
        // API retained for hit callers; no full-screen pulse or brightness change.
        public static void Pulse(float strength) { }
        void Update()
        {
            bool on=FX.Style==FX.VfxStyle.Neon;
            if(vol) vol.weight=Mathf.MoveTowards(vol.weight,on?1:0,Time.unscaledDeltaTime*4);
            if(canvas) canvas.enabled=on;
            if(!warning) return;
            var p=Combat.player;
            string message="";
            if(p && p.Downed) message="生命体征危急 · 等待队友救援";
            else if(p && p.dmg && p.dmg.hp < p.dmg.maxHp*.25f) message="生命值过低 · 寻找治疗";
            else if(Combat.boss && Combat.boss.ChantProgress>.01f) message="首领正在咏唱 · 打断施法";
            else if(Coop.erosion>80) message="黑潮侵蚀严重";
            warning.text=message;
        }
        void OnDestroy()
        {
            if(I==this) I=null;
            if(vol && vol.sharedProfile) Destroy(vol.sharedProfile);
        }
    }
}
