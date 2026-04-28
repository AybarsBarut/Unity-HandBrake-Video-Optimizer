#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityHandBrakeVideoOptimizer
{
    public class VideoOptimizerWindow : EditorWindow
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4",
            ".mov",
            ".mkv",
            ".avi",
            ".webm"
        };

        private static readonly VideoOptimizerPreset[] PresetValues =
        {
            VideoOptimizerPreset.Balanced,
            VideoOptimizerPreset.HighQuality,
            VideoOptimizerPreset.SmallSize,
            VideoOptimizerPreset.MobileWeb
        };

        private static readonly string[] PresetLabels =
        {
            "Balanced",
            "High Quality",
            "Small Size",
            "Mobile/Web"
        };

        private static readonly VideoOptimizerCodec[] CodecValues =
        {
            VideoOptimizerCodec.H264,
            VideoOptimizerCodec.H265
        };

        private static readonly string[] CodecLabels =
        {
            "H.264",
            "H.265"
        };

        private static readonly int[] AudioBitrateValues = { 96, 128, 160, 192, 256 };
        private static readonly string[] AudioBitrateLabels = { "96 kbps", "128 kbps", "160 kbps", "192 kbps", "256 kbps" };
        private const int MaxLogCharacters = 60000;

        private readonly List<VideoFileInfo> videos = new List<VideoFileInfo>();
        private readonly List<string> failedFiles = new List<string>();
        private readonly Queue<string> pendingLogLines = new Queue<string>();
        private readonly object pendingLogLock = new object();
        private readonly StringBuilder logBuilder = new StringBuilder();

        private Vector2 videoScroll;
        private Vector2 logScroll;
        private CancellationTokenSource cancellationTokenSource;
        private bool isProcessing;
        private int processedCount;
        private int totalToProcess;
        private float progress;
        private string statusText = "Ready";

        private VideoOptimizerSettings Settings
        {
            get { return VideoOptimizerSettings.instance; }
        }

        private string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        [MenuItem("Tools/Video Optimizer")]
        public static void Open()
        {
            VideoOptimizerWindow window = GetWindow<VideoOptimizerWindow>("Video Optimizer");
            window.minSize = new Vector2(760f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;

            if (string.IsNullOrEmpty(Settings.HandBrakeCliPath) && HandBrakeRunner.TryFindHandBrakeCli(out string foundPath))
            {
                Settings.HandBrakeCliPath = foundPath;
                AppendLog("HandBrakeCLI found: " + foundPath);
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;

            if (isProcessing && cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
            }
        }

        private void OnGUI()
        {
            DrawSettingsPanel();
            EditorGUILayout.Space(6f);
            DrawVideoList();
            EditorGUILayout.Space(6f);
            DrawProgressPanel();
            EditorGUILayout.Space(6f);
            DrawLogPanel();
        }

        private void DrawSettingsPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("HandBrakeCLI", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(isProcessing);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string newPath = EditorGUILayout.TextField("Path", Settings.HandBrakeCliPath);
            if (EditorGUI.EndChangeCheck())
            {
                Settings.HandBrakeCliPath = newPath;
            }

            if (GUILayout.Button("Browse", GUILayout.Width(80f)))
            {
                BrowseForHandBrakeCli();
            }

            if (GUILayout.Button("Auto Find", GUILayout.Width(90f)))
            {
                AutoFindHandBrakeCli(true);
            }
            EditorGUILayout.EndHorizontal();

            if (!HandBrakeRunner.IsHandBrakePathValid(Settings.HandBrakeCliPath))
            {
                EditorGUILayout.HelpBox("HandBrakeCLI was not found. Install HandBrakeCLI and set the executable path (" + HandBrakeRunner.ExpectedExecutableName + ").", MessageType.Error);
            }

            EditorGUILayout.Space(4f);
            DrawEncodingOptions();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();
        }

        private void DrawEncodingOptions()
        {
            EditorGUILayout.BeginHorizontal();

            int presetIndex = Mathf.Max(0, Array.IndexOf(PresetValues, Settings.Preset));
            EditorGUI.BeginChangeCheck();
            int newPresetIndex = EditorGUILayout.Popup("Preset", presetIndex, PresetLabels);
            if (EditorGUI.EndChangeCheck())
            {
                Settings.ApplyPreset(PresetValues[newPresetIndex], Settings.Codec);
            }

            int codecIndex = Mathf.Max(0, Array.IndexOf(CodecValues, Settings.Codec));
            EditorGUI.BeginChangeCheck();
            int newCodecIndex = EditorGUILayout.Popup("Codec", codecIndex, CodecLabels);
            if (EditorGUI.EndChangeCheck())
            {
                Settings.ApplyPreset(Settings.Preset, CodecValues[newCodecIndex]);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            float minRf = Settings.Codec == VideoOptimizerCodec.H265 ? 18f : 16f;
            float maxRf = Settings.Codec == VideoOptimizerCodec.H265 ? 30f : 28f;
            Settings.RfQuality = EditorGUILayout.Slider("RF / CRF Quality", Settings.RfQuality, minRf, maxRf);
            GUILayout.Label(Settings.RfQuality.ToString("0.#"), GUILayout.Width(36f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("Lower RF means higher quality and larger files. Suggested defaults: H.264 RF 20-23, H.265 RF 22-26.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            Settings.PreserveFps = EditorGUILayout.Toggle("Keep Source FPS", Settings.PreserveFps);
            Settings.AudioBitrate = EditorGUILayout.IntPopup("Audio Bitrate", Settings.AudioBitrate, AudioBitrateLabels, AudioBitrateValues);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string newOutputFolder = EditorGUILayout.TextField("Output Folder", Settings.OutputFolder);
            if (EditorGUI.EndChangeCheck())
            {
                Settings.OutputFolder = newOutputFolder;
            }

            if (GUILayout.Button("Select", GUILayout.Width(80f)))
            {
                BrowseForOutputFolder();
            }
            EditorGUILayout.EndHorizontal();

            Settings.OverwriteOriginal = EditorGUILayout.Toggle("Overwrite Original", Settings.OverwriteOriginal);
            if (Settings.OverwriteOriginal)
            {
                EditorGUILayout.HelpBox("Overwrite encodes to a temporary file first. The original is changed only after HandBrakeCLI succeeds.", MessageType.Info);
            }
        }

        private void DrawVideoList()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Project Videos", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(isProcessing);
            if (GUILayout.Button("Scan Project Videos", GUILayout.Width(160f)))
            {
                ScanProjectVideos();
            }

            if (GUILayout.Button("All", GUILayout.Width(44f)))
            {
                SetAllVideoSelection(true);
            }

            if (GUILayout.Button("None", GUILayout.Width(52f)))
            {
                SetAllVideoSelection(false);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(videos.Count + " supported video file(s) found under Assets.");

            DrawVideoListHeader();

            videoScroll = EditorGUILayout.BeginScrollView(videoScroll, GUILayout.MinHeight(150f), GUILayout.MaxHeight(240f));
            for (int i = 0; i < videos.Count; i++)
            {
                DrawVideoRow(videos[i]);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawVideoListHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Use", GUILayout.Width(34f));
            GUILayout.Label("File", GUILayout.MinWidth(220f));
            GUILayout.Label("Size", GUILayout.Width(92f));
            GUILayout.Label("Format", GUILayout.Width(58f));
            GUILayout.Label("Last Result", GUILayout.Width(170f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawVideoRow(VideoFileInfo video)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(isProcessing);
            video.IsSelected = EditorGUILayout.Toggle(video.IsSelected, GUILayout.Width(34f));
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.LabelField(new GUIContent(video.FileName, video.AssetPath), GUILayout.MinWidth(220f));
            EditorGUILayout.LabelField(VideoFileInfo.FormatBytes(video.OriginalSizeBytes), GUILayout.Width(92f));
            EditorGUILayout.LabelField(video.Format, GUILayout.Width(58f));

            Color previousColor = GUI.color;
            if (video.HasResult && !video.LastRunSucceeded)
            {
                GUI.color = new Color(1f, 0.65f, 0.65f);
            }
            else if (video.HasResult && video.LastRunSucceeded)
            {
                GUI.color = new Color(0.65f, 1f, 0.72f);
            }

            EditorGUILayout.LabelField(GetResultLabel(video), GUILayout.Width(170f));
            GUI.color = previousColor;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawProgressPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Rect progressRect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(progressRect, Mathf.Clamp01(progress), statusText);

            EditorGUILayout.BeginHorizontal();
            bool canOptimize = !isProcessing && GetSelectedCount() > 0 && HandBrakeRunner.IsHandBrakePathValid(Settings.HandBrakeCliPath);
            EditorGUI.BeginDisabledGroup(!canOptimize);
            if (GUILayout.Button("Optimize Selected", GUILayout.Height(26f)))
            {
                StartOptimization();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!isProcessing);
            if (GUILayout.Button("Cancel", GUILayout.Width(100f), GUILayout.Height(26f)))
            {
                CancelOptimization();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (!isProcessing && GetSelectedCount() == 0)
            {
                EditorGUILayout.HelpBox("Select at least one video to optimize.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLogPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", GUILayout.Width(70f)))
            {
                logBuilder.Length = 0;
                failedFiles.Clear();
            }
            EditorGUILayout.EndHorizontal();

            if (failedFiles.Count > 0)
            {
                EditorGUILayout.HelpBox("Failed files:\n" + string.Join("\n", failedFiles.ToArray()), MessageType.Warning);
            }

            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(120f));
            EditorGUILayout.TextArea(logBuilder.ToString(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void BrowseForHandBrakeCli()
        {
            string extension = Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "";
            string selectedPath = EditorUtility.OpenFilePanel("Select HandBrakeCLI", "", extension);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                Settings.HandBrakeCliPath = selectedPath;
                AppendLog("HandBrakeCLI path set: " + selectedPath);
            }
        }

        private void AutoFindHandBrakeCli(bool showLog)
        {
            if (HandBrakeRunner.TryFindHandBrakeCli(out string foundPath))
            {
                Settings.HandBrakeCliPath = foundPath;
                AppendLog("HandBrakeCLI found: " + foundPath);
            }
            else if (showLog)
            {
                AppendLog("HandBrakeCLI could not be found automatically. Set the path manually.");
            }
        }

        private void BrowseForOutputFolder()
        {
            string currentFolder = GetAbsoluteOutputFolder(Settings.OutputFolder);
            string selectedFolder = EditorUtility.OpenFolderPanel("Select Output Folder", currentFolder, "");
            if (string.IsNullOrEmpty(selectedFolder))
            {
                return;
            }

            Settings.OutputFolder = MakeProjectRelativeIfPossible(selectedFolder);
            AppendLog("Output folder set: " + Settings.OutputFolder);
        }

        private void ScanProjectVideos()
        {
            videos.Clear();
            failedFiles.Clear();

            try
            {
                string assetsRoot = Application.dataPath;
                string projectRoot = ProjectRoot;
                string[] files = Directory.GetFiles(assetsRoot, "*.*", SearchOption.AllDirectories);

                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    string extension = Path.GetExtension(file);
                    if (!SupportedExtensions.Contains(extension))
                    {
                        continue;
                    }

                    FileInfo fileInfo = new FileInfo(file);
                    string assetPath = ToAssetPath(fileInfo.FullName, projectRoot);
                    videos.Add(new VideoFileInfo(assetPath, fileInfo.Length));
                }

                videos.Sort(delegate(VideoFileInfo left, VideoFileInfo right)
                {
                    return string.Compare(left.AssetPath, right.AssetPath, StringComparison.OrdinalIgnoreCase);
                });

                AppendLog("Scan complete. Found " + videos.Count + " supported video file(s).");
            }
            catch (Exception exception)
            {
                AppendLog("Scan failed: " + exception.Message);
            }

            Repaint();
        }

        private async void StartOptimization()
        {
            if (isProcessing)
            {
                return;
            }

            if (!HandBrakeRunner.IsHandBrakePathValid(Settings.HandBrakeCliPath))
            {
                AppendLog("HandBrakeCLI was not found. Set a valid path before optimizing.");
                return;
            }

            List<VideoFileInfo> selectedVideos = GetSelectedVideos();
            if (selectedVideos.Count == 0)
            {
                AppendLog("No videos selected.");
                return;
            }

            isProcessing = true;
            processedCount = 0;
            totalToProcess = selectedVideos.Count;
            progress = 0f;
            failedFiles.Clear();
            statusText = "Starting...";
            cancellationTokenSource = new CancellationTokenSource();

            AppendLog("Starting optimization for " + totalToProcess + " video(s).");

            try
            {
                for (int i = 0; i < selectedVideos.Count; i++)
                {
                    VideoFileInfo video = selectedVideos[i];
                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }

                    statusText = "Optimizing " + video.FileName + " (" + (i + 1) + "/" + totalToProcess + ")";
                    AppendLog("Optimizing: " + video.AssetPath);

                    HandBrakeOptions options = CreateOptions();
                    OptimizationResult result = await HandBrakeRunner.OptimizeAsync(video, options, cancellationTokenSource.Token);
                    DrainPendingLogs();

                    if (result.Cancelled)
                    {
                        video.SetResult(-1, false, "Cancelled.");
                        AppendLog("Cancelled while processing: " + video.AssetPath);
                        break;
                    }

                    if (result.Success)
                    {
                        video.SetResult(result.OutputSizeBytes, true, result.Message);
                        AppendLog(BuildSuccessLog(video, result.OutputPath));
                    }
                    else
                    {
                        video.SetResult(-1, false, result.Message);
                        failedFiles.Add(video.AssetPath + " - " + result.Message);
                        AppendLog("Failed: " + video.AssetPath + " - " + result.Message);
                    }

                    processedCount++;
                    progress = processedCount / Mathf.Max(1f, totalToProcess);
                    Repaint();
                }
            }
            catch (Exception exception)
            {
                AppendLog("Optimization stopped: " + exception.Message);
            }
            finally
            {
                isProcessing = false;
                statusText = cancellationTokenSource != null && cancellationTokenSource.IsCancellationRequested ? "Cancelled" : "Finished";
                progress = totalToProcess > 0 ? processedCount / Mathf.Max(1f, totalToProcess) : 0f;

                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Dispose();
                    cancellationTokenSource = null;
                }

                AssetDatabase.Refresh();
                AppendLog("AssetDatabase.Refresh() complete.");
                Repaint();
            }
        }

        private HandBrakeOptions CreateOptions()
        {
            return new HandBrakeOptions
            {
                HandBrakeCliPath = Settings.HandBrakeCliPath,
                Codec = Settings.Codec,
                QualityRf = Settings.RfQuality,
                PreserveFps = Settings.PreserveFps,
                AudioBitrate = Settings.AudioBitrate,
                OutputFolder = Settings.OutputFolder,
                OverwriteOriginal = Settings.OverwriteOriginal,
                ProjectRoot = ProjectRoot,
                Log = EnqueueLog
            };
        }

        private void CancelOptimization()
        {
            if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
            {
                AppendLog("Cancel requested.");
                statusText = "Cancelling...";
                cancellationTokenSource.Cancel();
            }
        }

        private void SetAllVideoSelection(bool selected)
        {
            for (int i = 0; i < videos.Count; i++)
            {
                videos[i].IsSelected = selected;
            }
        }

        private List<VideoFileInfo> GetSelectedVideos()
        {
            List<VideoFileInfo> selected = new List<VideoFileInfo>();
            for (int i = 0; i < videos.Count; i++)
            {
                if (videos[i].IsSelected)
                {
                    selected.Add(videos[i]);
                }
            }

            return selected;
        }

        private int GetSelectedCount()
        {
            int count = 0;
            for (int i = 0; i < videos.Count; i++)
            {
                if (videos[i].IsSelected)
                {
                    count++;
                }
            }

            return count;
        }

        private string GetResultLabel(VideoFileInfo video)
        {
            if (!video.HasResult)
            {
                return "-";
            }

            if (!video.LastRunSucceeded)
            {
                return "Failed";
            }

            return VideoFileInfo.FormatBytes(video.OptimizedSizeBytes) + " saved " + video.GetSavingsText();
        }

        private string BuildSuccessLog(VideoFileInfo video, string outputPath)
        {
            return "Done: " + video.AssetPath
                + " | old " + VideoFileInfo.FormatBytes(video.OriginalSizeBytes)
                + " -> new " + VideoFileInfo.FormatBytes(video.OptimizedSizeBytes)
                + " | saved " + video.GetSavingsText()
                + " | output " + MakeProjectRelativeIfPossible(outputPath);
        }

        private void OnEditorUpdate()
        {
            bool hadLogs = DrainPendingLogs();
            if (hadLogs || isProcessing)
            {
                Repaint();
            }
        }

        private void EnqueueLog(string line)
        {
            lock (pendingLogLock)
            {
                pendingLogLines.Enqueue(line);
            }
        }

        private bool DrainPendingLogs()
        {
            bool hadLogs = false;

            while (true)
            {
                string line = null;
                lock (pendingLogLock)
                {
                    if (pendingLogLines.Count > 0)
                    {
                        line = pendingLogLines.Dequeue();
                    }
                }

                if (line == null)
                {
                    break;
                }

                AppendLog(line);
                hadLogs = true;
            }

            return hadLogs;
        }

        private void AppendLog(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            logBuilder.Append("[");
            logBuilder.Append(DateTime.Now.ToString("HH:mm:ss"));
            logBuilder.Append("] ");
            logBuilder.AppendLine(message);

            if (logBuilder.Length > MaxLogCharacters)
            {
                logBuilder.Remove(0, logBuilder.Length - MaxLogCharacters);
            }

            logScroll.y = float.MaxValue;
        }

        private string GetAbsoluteOutputFolder(string configuredFolder)
        {
            if (string.IsNullOrEmpty(configuredFolder))
            {
                configuredFolder = "Assets/OptimizedVideos";
            }

            if (Path.IsPathRooted(configuredFolder))
            {
                return configuredFolder;
            }

            return Path.GetFullPath(Path.Combine(ProjectRoot, configuredFolder.Replace('/', Path.DirectorySeparatorChar)));
        }

        private string MakeProjectRelativeIfPossible(string path)
        {
            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string projectRoot = Path.GetFullPath(ProjectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = fullPath.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            }

            return fullPath;
        }

        private static string ToAssetPath(string fullPath, string projectRoot)
        {
            string normalizedFullPath = Path.GetFullPath(fullPath);
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string relativePath = normalizedFullPath.Substring(normalizedProjectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }
    }
}
#endif
