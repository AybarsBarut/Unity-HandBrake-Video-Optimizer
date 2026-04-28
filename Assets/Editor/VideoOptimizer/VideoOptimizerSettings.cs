#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityHandBrakeVideoOptimizer
{
    public enum VideoOptimizerPreset
    {
        Balanced,
        HighQuality,
        SmallSize,
        MobileWeb
    }

    public enum VideoOptimizerCodec
    {
        H264,
        H265
    }

    [FilePath("ProjectSettings/VideoOptimizerSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class VideoOptimizerSettings : ScriptableSingleton<VideoOptimizerSettings>
    {
        [SerializeField] private string handBrakeCliPath = "";
        [SerializeField] private VideoOptimizerPreset preset = VideoOptimizerPreset.Balanced;
        [SerializeField] private VideoOptimizerCodec codec = VideoOptimizerCodec.H264;
        [SerializeField] private float rfQuality = 22f;
        [SerializeField] private bool preserveFps = true;
        [SerializeField] private int audioBitrate = 160;
        [SerializeField] private string outputFolder = "Assets/OptimizedVideos";
        [SerializeField] private bool overwriteOriginal;

        public string HandBrakeCliPath
        {
            get { return handBrakeCliPath; }
            set { SetAndSave(ref handBrakeCliPath, value); }
        }

        public VideoOptimizerPreset Preset
        {
            get { return preset; }
            set { SetAndSave(ref preset, value); }
        }

        public VideoOptimizerCodec Codec
        {
            get { return codec; }
            set { SetAndSave(ref codec, value); }
        }

        public float RfQuality
        {
            get { return rfQuality; }
            set { SetAndSave(ref rfQuality, value); }
        }

        public bool PreserveFps
        {
            get { return preserveFps; }
            set { SetAndSave(ref preserveFps, value); }
        }

        public int AudioBitrate
        {
            get { return audioBitrate; }
            set { SetAndSave(ref audioBitrate, value); }
        }

        public string OutputFolder
        {
            get { return outputFolder; }
            set { SetAndSave(ref outputFolder, value); }
        }

        public bool OverwriteOriginal
        {
            get { return overwriteOriginal; }
            set { SetAndSave(ref overwriteOriginal, value); }
        }

        public void ApplyPreset(VideoOptimizerPreset selectedPreset, VideoOptimizerCodec selectedCodec)
        {
            preset = selectedPreset;
            codec = selectedCodec;
            rfQuality = GetRecommendedRf(selectedPreset, selectedCodec);
            audioBitrate = GetRecommendedAudioBitrate(selectedPreset);
            SaveSettings();
        }

        public void SaveSettings()
        {
            Save(true);
        }

        public static float GetRecommendedRf(VideoOptimizerPreset selectedPreset, VideoOptimizerCodec selectedCodec)
        {
            bool h265 = selectedCodec == VideoOptimizerCodec.H265;

            switch (selectedPreset)
            {
                case VideoOptimizerPreset.HighQuality:
                    return h265 ? 22f : 20f;
                case VideoOptimizerPreset.SmallSize:
                    return h265 ? 26f : 23f;
                case VideoOptimizerPreset.MobileWeb:
                    return h265 ? 25f : 23f;
                case VideoOptimizerPreset.Balanced:
                default:
                    return h265 ? 24f : 22f;
            }
        }

        public static int GetRecommendedAudioBitrate(VideoOptimizerPreset selectedPreset)
        {
            switch (selectedPreset)
            {
                case VideoOptimizerPreset.SmallSize:
                case VideoOptimizerPreset.MobileWeb:
                    return 128;
                case VideoOptimizerPreset.HighQuality:
                case VideoOptimizerPreset.Balanced:
                default:
                    return 160;
            }
        }

        private void SetAndSave<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            SaveSettings();
        }
    }
}
#endif
