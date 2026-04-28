#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;

namespace UnityHandBrakeVideoOptimizer
{
    [Serializable]
    public class VideoFileInfo
    {
        public bool IsSelected = true;
        public string AssetPath;
        public long OriginalSizeBytes;
        public long OptimizedSizeBytes = -1;
        public bool HasResult;
        public bool LastRunSucceeded;
        public string LastMessage;

        public VideoFileInfo(string assetPath, long originalSizeBytes)
        {
            AssetPath = assetPath;
            OriginalSizeBytes = originalSizeBytes;
        }

        public string FileName
        {
            get { return Path.GetFileName(AssetPath); }
        }

        public string Extension
        {
            get { return Path.GetExtension(AssetPath).ToLowerInvariant(); }
        }

        public string Format
        {
            get
            {
                string extension = Extension.TrimStart('.');
                return string.IsNullOrEmpty(extension) ? "UNKNOWN" : extension.ToUpperInvariant();
            }
        }

        public string GetAbsolutePath(string projectRoot)
        {
            string normalizedAssetPath = AssetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(projectRoot, normalizedAssetPath));
        }

        public void SetResult(long optimizedSizeBytes, bool succeeded, string message)
        {
            OptimizedSizeBytes = optimizedSizeBytes;
            LastRunSucceeded = succeeded;
            LastMessage = message;
            HasResult = true;
        }

        public string GetSavingsText()
        {
            if (!HasResult || OptimizedSizeBytes < 0 || OriginalSizeBytes <= 0)
            {
                return "-";
            }

            double saved = OriginalSizeBytes - OptimizedSizeBytes;
            double percent = saved / OriginalSizeBytes * 100.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:0.##}%", percent);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0)
            {
                return "-";
            }

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024.0 && unitIndex < units.Length - 1)
            {
                size /= 1024.0;
                unitIndex++;
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", size, units[unitIndex]);
        }
    }
}
#endif
