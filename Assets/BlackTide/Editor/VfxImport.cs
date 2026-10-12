using System.IO;
using UnityEditor;
using UnityEngine;

namespace BlackTide
{
    // DS-010 收尾：把 E:\heichai\特效 的三张高优先贴图导入 Resources/VFX 并设置导入参数（可重复执行）。
    // 用法：-batchmode -quit -projectPath <工程> -executeMethod BlackTide.VfxImport.Run -logFile <log>
    public static class VfxImport
    {
        // DS-018c：第二批——召唤圈/黑潮爆发/符文环/声波环/火焰/预警圈/冲击爆炸/尘雾/黑潮飞溅/治疗微粒
        // CL-003（2026-10-11 特效优先）：第三批——重击爆点/余烬/烟/冰片/易伤标记/锁链/冰面/掉落光柱
        // DS-025（DeepSeek 收尾）：第四批——命中星/暴击爆/五种刀光形/两种冲击环/地裂（导入后供「大一点、炸一点」用）
        static readonly string[] Files = { "V16_lightning_strip", "V32_lightning_strike", "V19_shield_bubble", "V34_summoning_circle", "V31_black_tide_burst", "V11_wave_rune_ring", "V18_soundwave_rings", "V24_burning_flame", "V25_danger_warning_circle", "V30_impact_explosion", "V33_dust_cloud", "V13_black_tide_splash", "V20_healing_sparkles",
            "V03_heavy_impact", "V14_ember_particle", "V15_smoke_puff", "V17_ice_shards", "V21_vulnerability_reticle", "V22_binding_chains", "V23_frozen_ground", "V26_loot_light_column",
            "V01_hit_star", "V02_critical_burst", "V04_horizontal_slash", "V05_vertical_slash", "V06_spinning_slash", "V07_dual_quick_slash", "V08_piercing_thrust", "V09_shockwave_ring_clean", "V10_shockwave_ring_broken", "V12_ground_cracks" };

        public static void Run()
        {
            string src = @"E:\heichai\特效";
            string dst = Path.GetFullPath(Path.Combine(Application.dataPath, "BlackTide", "Resources", "VFX"));
            if (!Directory.Exists(dst)) Directory.CreateDirectory(dst);
            foreach (var n in Files)
            {
                var s = Path.Combine(src, n + ".png");
                if (!File.Exists(s)) { Debug.LogWarning("[VfxImport] missing " + s); continue; }
                File.Copy(s, Path.Combine(dst, n + ".png"), true);
                Debug.Log("[VfxImport] copied " + n);
            }
            AssetDatabase.Refresh();
            foreach (var n in Files)
            {
                var p = "Assets/BlackTide/Resources/VFX/" + n + ".png";
                var imp = (TextureImporter)AssetImporter.GetAtPath(p);
                if (imp == null) { Debug.LogWarning("[VfxImport] importer null " + p); continue; }
                imp.textureType = TextureImporterType.Default;
                imp.mipmapEnabled = false;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.isReadable = true;
                imp.SaveAndReimport();
                Debug.Log("[VfxImport] imported " + p);
            }
            Debug.Log("VFXIMPORT_DONE");
        }
    }
}
