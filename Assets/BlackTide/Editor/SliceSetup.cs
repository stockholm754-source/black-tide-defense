using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BlackTide.EditorTools
{
    // 一键搭建：导入设置 → 动画控制器 → URP → 资产引用 → 场景
    public static class SliceSetup
    {
        const string Root = "Assets/BlackTide";
        static readonly string[] Models = { "Art/Characters/Knight.fbx", "Art/Characters/Skeleton_Warrior.fbx", "Art/Characters/Skeleton_Minion.fbx" };
        static readonly string[] Weapons = { "Art/Weapons/Skeleton_Axe.fbx", "Art/Weapons/Skeleton_Blade.fbx" };
        static readonly string[] LoopClips = { "Idle", "Idle_B", "Idle_Combat", "2H_Melee_Idle", "Unarmed_Idle", "Running_A", "Running_B", "Running_C",
            "Running_Strafe_Left", "Running_Strafe_Right", "Walking_A", "Walking_B", "Walking_C", "Walking_D_Skeletons", "Walking_Backwards", "Spellcasting", "Blocking", "2H_Melee_Attack_Spinning" };

        [MenuItem("BlackTide/Setup Everything")]
        public static void SetupAll()
        {
            ImportSettings();
            BuildControllers();
            SetupURP();
            BuildAssetRefs();
            BuildScene();
            Debug.Log("BLACKTIDE_SETUP_DONE");
        }

        static string Clean(string n) => n.Contains("|") ? n.Substring(n.LastIndexOf('|') + 1) : n;

        [MenuItem("BlackTide/Steps/1 Import Settings")]
        public static void ImportSettings()
        {
            foreach (var rel in Models)
            {
                var p = $"{Root}/{rel}";
                var imp = (ModelImporter)AssetImporter.GetAtPath(p);
                if (imp == null) { Debug.LogError("Missing model " + p); continue; }
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.isReadable = true;   // 武器对齐要读剑、斧的网格顶点；开局后才生成的 Boss 读不到未开读写的网格
                var clips = imp.defaultClipAnimations;
                foreach (var c in clips)
                {
                    c.name = Clean(c.name);
                    c.loopTime = LoopClips.Contains(c.name);
                    c.lockRootPositionXZ = false;
                }
                imp.clipAnimations = clips;
                imp.SaveAndReimport();
            }
            foreach (var rel in Weapons)
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath($"{Root}/{rel}");
                if (imp == null) continue;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false;
                imp.isReadable = true;
                imp.SaveAndReimport();
            }
            SetTex("Art/Textures/T_Flagstone_Normal.png", t => { t.textureType = TextureImporterType.NormalMap; });
            SetTex("Art/Textures/T_Flagstone_Albedo.png", t => { t.wrapMode = TextureWrapMode.Repeat; t.anisoLevel = 4; });
            SetTex("Art/Textures/T_TideNoise.png", t => { t.wrapMode = TextureWrapMode.Repeat; t.sRGBTexture = false; });
            SetTex("Art/Textures/T_SoftDot.png", t => { t.wrapMode = TextureWrapMode.Clamp; t.alphaIsTransparency = true; });
            SetTex("Art/Textures/T_SlashStreak.png", t => { t.wrapMode = TextureWrapMode.Clamp; t.alphaIsTransparency = true; });
        }

        static void SetTex(string rel, Action<TextureImporter> f)
        {
            var t = (TextureImporter)AssetImporter.GetAtPath($"{Root}/{rel}");
            if (t == null) { Debug.LogError("Missing texture " + rel); return; }
            f(t);
            t.SaveAndReimport();
        }

        [MenuItem("BlackTide/Steps/2 Animator Controllers")]
        public static void BuildControllers()
        {
            AssetDatabase.CreateFolder(Root, "Anim");
            foreach (var rel in Models)
            {
                var p = $"{Root}/{rel}";
                var name = System.IO.Path.GetFileNameWithoutExtension(p);
                var cpath = $"{Root}/Anim/{name}.controller";
                if (AssetDatabase.LoadAssetAtPath<AnimatorController>(cpath)) AssetDatabase.DeleteAsset(cpath);
                var ctrl = AnimatorController.CreateAnimatorControllerAtPath(cpath);
                var sm = ctrl.layers[0].stateMachine;
                var clips = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
                AnimatorState def = null;
                foreach (var c in clips)
                {
                    var st = sm.AddState(c.name);
                    st.motion = c;
                    st.writeDefaultValues = true;
                    if (c.name == "Idle" || c.name == "2H_Melee_Idle") def = st;
                }
                if (def != null) sm.defaultState = def;
                EditorUtility.SetDirty(ctrl);
                Debug.Log($"Controller {name}: {clips.Length} states");
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("BlackTide/Steps/3 URP")]
        public static void SetupURP()
        {
            AssetDatabase.CreateFolder(Root, "Settings");
            string assetPath = $"{Root}/Settings/BT_URP.asset";
            string dataPath = $"{Root}/Settings/BT_URP_Renderer.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (asset == null)
            {
                ScriptableRendererData data = null;
                // 用 URP 自己的内部方法创建渲染器数据，保证着色器等资源被正确填充
                var m = typeof(UniversalRenderPipelineAsset).GetMethod("CreateRendererAsset", BindingFlags.NonPublic | BindingFlags.Static);
                if (m != null)
                {
                    var ps = m.GetParameters();
                    var rtype = Enum.Parse(ps[1].ParameterType, "UniversalRenderer");
                    var args = new object[ps.Length];
                    args[0] = dataPath; args[1] = rtype;
                    if (ps.Length > 2) args[2] = false;
                    for (int i = 3; i < ps.Length; i++) args[i] = ps[i].DefaultValue;
                    data = (ScriptableRendererData)m.Invoke(null, args);
                }
                else Debug.LogError("CreateRendererAsset not found");
                asset = UniversalRenderPipelineAsset.Create(data);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            asset.shadowDistance = 55f;
            asset.msaaSampleCount = 4;
            asset.supportsHDR = true;
            var so = new SerializedObject(asset);
            SetProp(so, "m_SoftShadowsSupported", true);
            SetProp(so, "m_MainLightShadowmapResolution", 4096);
            SetProp(so, "m_ShadowCascadeCount", 2);
            SetProp(so, "m_AdditionalLightsRenderingMode", 1);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            int cur = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(cur, false);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
            Debug.Log("URP ready: " + assetPath);
        }

        static void SetProp(SerializedObject so, string name, object v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning("URP prop not found " + name); return; }
            if (v is bool b) p.boolValue = b;
            else if (v is int i) { if (p.propertyType == SerializedPropertyType.Enum) p.intValue = i; else p.intValue = i; }
        }

        [MenuItem("BlackTide/Steps/4 Asset Refs")]
        public static void BuildAssetRefs()
        {
            AssetDatabase.CreateFolder(Root, "Resources");
            string path = $"{Root}/Resources/SliceAssets.asset";
            var a = AssetDatabase.LoadAssetAtPath<SliceAssets>(path);
            if (a == null) { a = ScriptableObject.CreateInstance<SliceAssets>(); AssetDatabase.CreateAsset(a, path); }
            T L<T>(string rel) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>($"{Root}/{rel}");
            a.heroModel = L<GameObject>("Art/Characters/Knight.fbx");
            a.bossModel = L<GameObject>("Art/Characters/Skeleton_Warrior.fbx");
            a.minionModel = L<GameObject>("Art/Characters/Skeleton_Minion.fbx");
            a.axeModel = L<GameObject>("Art/Weapons/Skeleton_Axe.fbx");
            a.bladeModel = L<GameObject>("Art/Weapons/Skeleton_Blade.fbx");
            a.heroController = L<RuntimeAnimatorController>("Anim/Knight.controller");
            a.bossController = L<RuntimeAnimatorController>("Anim/Skeleton_Warrior.controller");
            a.minionController = L<RuntimeAnimatorController>("Anim/Skeleton_Minion.controller");
            a.heroTexture = L<Texture2D>("Art/Characters/knight_texture.png");
            a.skeletonTexture = L<Texture2D>("Art/Characters/skeleton_texture.png");
            a.groundAlbedo = L<Texture2D>("Art/Textures/T_Flagstone_Albedo.png");
            a.groundNormal = L<Texture2D>("Art/Textures/T_Flagstone_Normal.png");
            a.tideNoise = L<Texture2D>("Art/Textures/T_TideNoise.png");
            a.softDot = L<Texture2D>("Art/Textures/T_SoftDot.png");
            a.slashStreak = L<Texture2D>("Art/Textures/T_SlashStreak.png");
            a.additiveShader = L<Shader>("Shaders/BT_Additive.shader");
            a.telegraphShader = L<Shader>("Shaders/BT_Telegraph.shader");
            a.tideShader = L<Shader>("Shaders/BT_Tide.shader");
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            var missing = typeof(SliceAssets).GetFields().Where(f => f.GetValue(a) == null).Select(f => f.Name).ToArray();
            Debug.Log(missing.Length == 0 ? "SliceAssets complete" : "SliceAssets missing: " + string.Join(",", missing));
        }

        // 开放地图一局：和竞技场同一套入口（SliceGame），打开 runMode
        [MenuItem("BlackTide/Build Run Scene (开放地图)")]
        public static void BuildRunScene()
        {
            const string path = "Assets/BlackTide/Scenes/BlackTide_Run.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 260f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.AddComponent<CameraRig>();
            new GameObject("GameRoot").AddComponent<SliceGame>().runMode = true;
            EditorSceneManager.SaveScene(scene, path);
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(e => e.path == path)) list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("RUN_SCENE_DONE " + path);
        }

        [MenuItem("BlackTide/Steps/5 Build Scene")]
        public static void BuildScene()
        {
            AssetDatabase.CreateFolder(Root, "Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 33f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.05f, 0.11f);
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.AddComponent<CameraRig>();
            camGo.transform.SetPositionAndRotation(new Vector3(0, 17, -21), Quaternion.Euler(52, 0, 0));
            new GameObject("GameRoot").AddComponent<SliceGame>();
            string path = $"{Root}/Scenes/BlackTide_Slice.unity";
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            Debug.Log("Scene saved " + path);
        }
    }
}
