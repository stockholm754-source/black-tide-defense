using UnityEditor;
using UnityEngine;

namespace BlackTide.EditorTools
{
    public sealed class AudioImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            bool music=assetPath.StartsWith("Assets/BlackTide/Resources/Audio/Music/");
            if(!music && !assetPath.StartsWith("Assets/BlackTide/Resources/SFX/")) return;
            var importer=(AudioImporter)assetImporter;
            importer.forceToMono=!music; importer.loadInBackground=music;
            var settings=importer.defaultSampleSettings;
            // DSH 修复（2026-10-11 19:2x，Claude 音频导入编译错误）：
            //  · preloadAudioData 已迁到 AudioImporterSampleSettings（UnityEditor.xml 证实）
            //  · 三个枚举的真实命名空间用本机 XML 文档定位：AudioClipLoadType/AudioCompressionFormat 在 UnityEngine，
            //    AudioSampleRateSetting 在 UnityEditor（团结 1.10 分支与旧版 Unity 不同）
            settings.preloadAudioData=!music;
            settings.loadType=music ? UnityEngine.AudioClipLoadType.Streaming : UnityEngine.AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat=music ? UnityEngine.AudioCompressionFormat.Vorbis : UnityEngine.AudioCompressionFormat.PCM;
            settings.quality=.8f;
            settings.sampleRateSetting=AudioSampleRateSetting.OverrideSampleRate; settings.sampleRateOverride=44100;
            importer.defaultSampleSettings=settings;
        }
    }
}
