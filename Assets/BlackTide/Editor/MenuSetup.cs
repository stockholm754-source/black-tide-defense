using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlackTide
{
    // DS-013 主菜单场景搭建：把黑潮之心原图与图标拷进 Resources/Menu，创建 MainMenu 场景并置为构建第一场景。
    // 用法：-batchmode -quit -projectPath <工程> -executeMethod BlackTide.MenuSetup.Run -logFile <log>
    public static class MenuSetup
    {
        public static void Run()
        {
            // 1) 素材 → Resources/Menu
            const string resDir = "Assets/BlackTide/Resources/Menu";
            if (!AssetDatabase.IsValidFolder(resDir)) AssetDatabase.CreateFolder("Assets/BlackTide/Resources", "Menu");
            string dstDir = Path.GetFullPath(Path.Combine(Application.dataPath, "BlackTide", "Resources", "Menu"));
            Directory.CreateDirectory(dstDir);
            File.Copy(@"E:\heichai\场景\11_tide_heart_core.png", Path.Combine(dstDir, "tide_heart.png"), true);
            AssetDatabase.CopyAsset("Assets/BlackTide/Art/GameIcon.png", resDir + "/icon.png");
            AssetDatabase.Refresh();

            // 2) 主菜单场景：相机（深潮底色）+ MainMenu 根
            const string path = "Assets/BlackTide/Scenes/MainMenu.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.016f, 0.043f, 0.062f);
            camGo.AddComponent<AudioListener>();
            new GameObject("MenuRoot").AddComponent<MainMenu>();
            EditorSceneManager.SaveScene(scene, path);

            // 3) 构建场景顺序：主菜单 → 开放地图一局
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(path, true),
                new EditorBuildSettingsScene("Assets/BlackTide/Scenes/BlackTide_Run.unity", true),
            };
            Debug.Log("MENU_SETUP_DONE " + path);
        }
    }
}
