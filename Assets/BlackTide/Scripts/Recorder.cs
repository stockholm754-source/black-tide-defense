using System.Collections;
using System.IO;
using UnityEngine;

namespace BlackTide
{
    // 定帧录像：游戏以固定 30 帧推进，每帧把画面（含界面）存成 JPG。按 F9 录 8 秒，文件在工程目录 Captures/ 下
    public class Recorder : MonoBehaviour
    {
        public static bool Active;
        public static string LastFolder;
        static Recorder inst;
        int frame, total;
        string dir;
        RenderTexture rt;
        Texture2D tex;
        Camera cam;
        readonly System.Collections.Generic.List<Canvas> canvases = new System.Collections.Generic.List<Canvas>();   // 录的时候临时改成摄像机画布的那些

        public static void Begin(string name, float seconds, int width = 1280, int height = 720)
        {
            if (Active) return;
            if (!inst) inst = new GameObject("Recorder").AddComponent<Recorder>();
            inst.StartRec(name, Mathf.RoundToInt(seconds * 30f), width, height);
        }

        void StartRec(string name, int frames, int w, int h)
        {
            dir = Path.Combine(Application.dataPath, "..", "Captures", name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            LastFolder = Path.GetFullPath(dir);
            cam = Camera.main;
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            // 覆盖式画布不会画进渲染贴图，录的时候全部临时挂到摄像机上（跳字、界面各在各的画布里）
            canvases.Clear();
            foreach (var c in FindObjectsOfType<Canvas>())
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 1f; canvases.Add(c); }
            frame = 0; total = frames;
            Time.captureFramerate = 30;
            Active = true;
            StartCoroutine(Loop());
            Debug.Log("REC_START " + LastFolder);
        }

        IEnumerator Loop()
        {
            var wait = new WaitForEndOfFrame();
            while (frame < total)
            {
                yield return wait;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply(false);
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, $"f_{frame:0000}.jpg"), tex.EncodeToJPG(88));
                frame++;
            }
            Time.captureFramerate = 0;
            cam.targetTexture = null;
            foreach (var c in canvases) if (c) c.renderMode = RenderMode.ScreenSpaceOverlay;
            rt.Release();
            Active = false;
            Debug.Log("REC_DONE " + LastFolder + " frames=" + total);
        }

        void OnDisable() { Time.captureFramerate = 0; Active = false; }
    }
}
