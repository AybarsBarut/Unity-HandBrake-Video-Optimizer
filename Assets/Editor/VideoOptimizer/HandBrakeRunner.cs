#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnityHandBrakeVideoOptimizer
{
    public sealed class HandBrakeOptions
    {
        public string HandBrakeCliPath;
        public VideoOptimizerCodec Codec;
        public float QualityRf;
        public bool PreserveFps;
        public int AudioBitrate;
        public string OutputFolder;
        public bool OverwriteOriginal;
        public string ProjectRoot;
        public Action<string> Log;
    }

    public sealed class OptimizationResult
    {
        public bool Success;
        public bool Cancelled;
        public string InputPath;
        public string OutputPath;
        public string Message;
        public long OriginalSizeBytes;
        public long OutputSizeBytes = -1;
        public int ExitCode;
    }

    public static class HandBrakeRunner
    {
        private const string DefaultOutputFolder = "Assets/OptimizedVideos";

        public static string ExpectedExecutableName
        {
            get
            {
#if UNITY_EDITOR_WIN
                return "HandBrakeCLI.exe";
#else
                return "HandBrakeCLI";
#endif
            }
        }

        public static bool IsHandBrakePathValid(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        public static bool TryFindHandBrakeCli(out string foundPath)
        {
            foundPath = null;
            string executableName = ExpectedExecutableName;
            List<string> candidates = new List<string>();

            string pathEnvironment = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnvironment))
            {
                string[] pathFolders = pathEnvironment.Split(Path.PathSeparator);
                for (int i = 0; i < pathFolders.Length; i++)
                {
                    string folder = pathFolders[i].Trim().Trim('"');
                    if (!string.IsNullOrEmpty(folder))
                    {
                        candidates.Add(Path.Combine(folder, executableName));
                    }
                }
            }

#if UNITY_EDITOR_WIN
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            AddCandidate(candidates, programFiles, "HandBrake", executableName);
            AddCandidate(candidates, programFilesX86, "HandBrake", executableName);
            AddCandidate(candidates, programFiles, "HandBrakeCLI", executableName);
#elif UNITY_EDITOR_OSX
            candidates.Add("/opt/homebrew/bin/HandBrakeCLI");
            candidates.Add("/usr/local/bin/HandBrakeCLI");
            candidates.Add("/Applications/HandBrakeCLI");
            candidates.Add("/Applications/HandBrake.app/Contents/MacOS/HandBrakeCLI");
#else
            candidates.Add("/usr/bin/HandBrakeCLI");
            candidates.Add("/usr/local/bin/HandBrakeCLI");
            candidates.Add("/snap/bin/handbrake-cli");
#endif

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                {
                    foundPath = candidate;
                    return true;
                }
            }

            return false;
        }

        public static async Task<OptimizationResult> OptimizeAsync(
            VideoFileInfo video,
            HandBrakeOptions options,
            CancellationToken cancellationToken)
        {
            OptimizationResult result = new OptimizationResult();
            string tempOutputPath = null;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                ValidateOptions(options);

                string inputPath = video.GetAbsolutePath(options.ProjectRoot);
                if (!File.Exists(inputPath))
                {
                    throw new FileNotFoundException("Input video was not found.", inputPath);
                }

                OutputPlan outputPlan = BuildOutputPlan(inputPath, options);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPlan.FinalOutputPath));

                tempOutputPath = CreateTempOutputPath(outputPlan.FinalOutputPath);
                DeleteIfExists(tempOutputPath);

                result.InputPath = inputPath;
                result.OutputPath = outputPlan.FinalOutputPath;
                result.OriginalSizeBytes = new FileInfo(inputPath).Length;

                if (!string.IsNullOrEmpty(outputPlan.Warning))
                {
                    SafeLog(options, outputPlan.Warning);
                }

                List<string> arguments = BuildArguments(inputPath, tempOutputPath, options);
                SafeLog(options, "Running: " + BuildCommandPreview(options.HandBrakeCliPath, arguments));

                int exitCode = await RunProcessAsync(options.HandBrakeCliPath, arguments, options.Log, cancellationToken);
                result.ExitCode = exitCode;

                cancellationToken.ThrowIfCancellationRequested();

                if (exitCode != 0)
                {
                    result.Success = false;
                    result.Message = "HandBrakeCLI failed with exit code " + exitCode + ".";
                    DeleteIfExists(tempOutputPath);
                    return result;
                }

                if (!File.Exists(tempOutputPath) || new FileInfo(tempOutputPath).Length <= 0)
                {
                    result.Success = false;
                    result.Message = "HandBrakeCLI finished, but no output file was produced.";
                    DeleteIfExists(tempOutputPath);
                    return result;
                }

                FinalizeOutput(inputPath, tempOutputPath, outputPlan, options.Log);
                tempOutputPath = null;

                result.OutputSizeBytes = new FileInfo(outputPlan.FinalOutputPath).Length;
                result.Success = true;
                result.Message = "Optimized successfully.";
                return result;
            }
            catch (OperationCanceledException)
            {
                DeleteIfExists(tempOutputPath);
                result.Cancelled = true;
                result.Success = false;
                result.Message = "Cancelled.";
                return result;
            }
            catch (Exception exception)
            {
                DeleteIfExists(tempOutputPath);
                result.Success = false;
                result.Message = exception.Message;
                return result;
            }
        }

        private static void ValidateOptions(HandBrakeOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            if (!IsHandBrakePathValid(options.HandBrakeCliPath))
            {
                throw new FileNotFoundException("HandBrakeCLI was not found. Set a valid HandBrakeCLI path before optimizing.");
            }

            if (string.IsNullOrEmpty(options.ProjectRoot) || !Directory.Exists(options.ProjectRoot))
            {
                throw new DirectoryNotFoundException("Unity project root was not found.");
            }
        }

        private static List<string> BuildArguments(string inputPath, string outputPath, HandBrakeOptions options)
        {
            List<string> arguments = new List<string>();
            arguments.Add("-i");
            arguments.Add(inputPath);
            arguments.Add("-o");
            arguments.Add(outputPath);
            arguments.Add("-e");
            arguments.Add(options.Codec == VideoOptimizerCodec.H265 ? "x265" : "x264");
            arguments.Add("-q");
            arguments.Add(options.QualityRf.ToString("0.#", CultureInfo.InvariantCulture));

            if (options.PreserveFps)
            {
                arguments.Add("--rate");
                arguments.Add("same");
            }

            arguments.Add("--audio");
            arguments.Add("1");
            arguments.Add("-E");
            arguments.Add("av_aac");
            arguments.Add("-B");
            arguments.Add(options.AudioBitrate.ToString(CultureInfo.InvariantCulture));

            if (Path.GetExtension(outputPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("--optimize");
            }

            return arguments;
        }

        private static async Task<int> RunProcessAsync(
            string executablePath,
            List<string> arguments,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource<int> exitCompletion = new TaskCompletionSource<int>();

            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = JoinArguments(arguments),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                process.EnableRaisingEvents = true;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        log?.Invoke(args.Data);
                    }
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        log?.Invoke(args.Data);
                    }
                };
                process.Exited += delegate
                {
                    try
                    {
                        exitCompletion.TrySetResult(process.ExitCode);
                    }
                    catch (Exception exception)
                    {
                        exitCompletion.TrySetException(exception);
                    }
                };

                using (cancellationToken.Register(delegate
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            log?.Invoke("Cancellation requested. Stopping HandBrakeCLI...");
                            process.Kill();
                        }
                    }
                    catch
                    {
                        // Process may not have started yet or may have already exited.
                    }
                }))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!process.Start())
                    {
                        throw new InvalidOperationException("HandBrakeCLI process could not be started.");
                    }

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    int exitCode = await exitCompletion.Task;
                    process.WaitForExit();
                    cancellationToken.ThrowIfCancellationRequested();
                    return exitCode;
                }
            }
        }

        private static OutputPlan BuildOutputPlan(string inputPath, HandBrakeOptions options)
        {
            string inputDirectory = Path.GetDirectoryName(inputPath);
            string inputName = Path.GetFileNameWithoutExtension(inputPath);
            string inputExtension = Path.GetExtension(inputPath);

            if (options.OverwriteOriginal)
            {
                if (inputExtension.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    return new OutputPlan
                    {
                        FinalOutputPath = inputPath,
                        ReplaceExistingFile = true
                    };
                }

                string convertedMp4Path = Path.Combine(inputDirectory, inputName + ".mp4");
                if (!File.Exists(convertedMp4Path))
                {
                    return new OutputPlan
                    {
                        FinalOutputPath = convertedMp4Path,
                        DeleteOriginalAfterSuccess = true,
                        MoveMetaFileToOutput = true,
                        Warning = "Source is not MP4. Optimized file will replace it as an MP4 after encoding succeeds."
                    };
                }

                return new OutputPlan
                {
                    FinalOutputPath = GetUniquePath(inputDirectory, inputName + "_optimized", ".mp4"),
                    Warning = "Overwrite requested, but an MP4 with the same base name already exists. Original file will be kept and an _optimized copy will be written."
                };
            }

            string outputDirectory = ResolveOutputFolder(options.ProjectRoot, options.OutputFolder);
            return new OutputPlan
            {
                FinalOutputPath = GetUniquePath(outputDirectory, inputName + "_optimized", ".mp4")
            };
        }

        private static void FinalizeOutput(string inputPath, string tempOutputPath, OutputPlan outputPlan, Action<string> log)
        {
            if (outputPlan.ReplaceExistingFile)
            {
                ReplaceFile(tempOutputPath, outputPlan.FinalOutputPath, log);
                return;
            }

            if (File.Exists(outputPlan.FinalOutputPath))
            {
                throw new IOException("Output file already exists: " + outputPlan.FinalOutputPath);
            }

            File.Move(tempOutputPath, outputPlan.FinalOutputPath);

            if (outputPlan.DeleteOriginalAfterSuccess)
            {
                DeleteOriginalAfterSuccessfulEncode(inputPath, outputPlan.FinalOutputPath, outputPlan.MoveMetaFileToOutput, log);
            }
        }

        private static void ReplaceFile(string sourcePath, string destinationPath, Action<string> log)
        {
            string backupPath = destinationPath + ".videooptimizer.bak";
            DeleteIfExists(backupPath);

            try
            {
                File.Replace(sourcePath, destinationPath, backupPath, true);
                DeleteIfExists(backupPath);
            }
            catch (Exception replaceException)
            {
                log?.Invoke("File.Replace failed, falling back to manual replace: " + replaceException.Message);

                try
                {
                    if (File.Exists(destinationPath))
                    {
                        File.Move(destinationPath, backupPath);
                    }

                    File.Move(sourcePath, destinationPath);
                    DeleteIfExists(backupPath);
                }
                catch
                {
                    if (!File.Exists(destinationPath) && File.Exists(backupPath))
                    {
                        File.Move(backupPath, destinationPath);
                    }

                    throw;
                }
            }
        }

        private static void DeleteOriginalAfterSuccessfulEncode(string inputPath, string outputPath, bool moveMetaFile, Action<string> log)
        {
            string sourceMetaPath = inputPath + ".meta";
            string outputMetaPath = outputPath + ".meta";

            File.Delete(inputPath);

            if (moveMetaFile && File.Exists(sourceMetaPath) && !File.Exists(outputMetaPath))
            {
                try
                {
                    File.Move(sourceMetaPath, outputMetaPath);
                }
                catch (Exception exception)
                {
                    log?.Invoke("Optimized file was written, but the Unity .meta file could not be moved: " + exception.Message);
                }
            }
        }

        private static string ResolveOutputFolder(string projectRoot, string outputFolder)
        {
            string folder = string.IsNullOrWhiteSpace(outputFolder) ? DefaultOutputFolder : outputFolder;
            if (Path.IsPathRooted(folder))
            {
                return Path.GetFullPath(folder);
            }

            return Path.GetFullPath(Path.Combine(projectRoot, folder.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string CreateTempOutputPath(string finalOutputPath)
        {
            string directory = Path.GetDirectoryName(finalOutputPath);
            string fileName = Path.GetFileNameWithoutExtension(finalOutputPath);
            string extension = Path.GetExtension(finalOutputPath);
            return Path.Combine(directory, fileName + ".videooptimizer.tmp" + extension);
        }

        private static string GetUniquePath(string directory, string baseName, string extension)
        {
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, baseName + extension);
            int index = 1;

            while (File.Exists(path))
            {
                path = Path.Combine(directory, baseName + "_" + index + extension);
                index++;
            }

            return path;
        }

        private static void DeleteIfExists(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void AddCandidate(List<string> candidates, string root, string folderName, string executableName)
        {
            if (!string.IsNullOrEmpty(root))
            {
                candidates.Add(Path.Combine(root, folderName, executableName));
            }
        }

        private static string BuildCommandPreview(string executablePath, List<string> arguments)
        {
            return QuoteArgument(executablePath) + " " + JoinArguments(arguments);
        }

        private static string JoinArguments(List<string> arguments)
        {
            string[] quoted = new string[arguments.Count];
            for (int i = 0; i < arguments.Count; i++)
            {
                quoted[i] = QuoteArgument(arguments[i]);
            }

            return string.Join(" ", quoted);
        }

        private static string QuoteArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "\"\"";
            }

            bool needsQuotes = argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) >= 0;
            if (!needsQuotes)
            {
                return argument;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append('"');

            int backslashCount = 0;
            for (int i = 0; i < argument.Length; i++)
            {
                char character = argument[i];
                if (character == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashCount * 2 + 1);
                    builder.Append('"');
                    backslashCount = 0;
                    continue;
                }

                if (backslashCount > 0)
                {
                    builder.Append('\\', backslashCount);
                    backslashCount = 0;
                }

                builder.Append(character);
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount * 2);
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static void SafeLog(HandBrakeOptions options, string message)
        {
            if (options != null && options.Log != null)
            {
                options.Log(message);
            }
        }

        private sealed class OutputPlan
        {
            public string FinalOutputPath;
            public bool ReplaceExistingFile;
            public bool DeleteOriginalAfterSuccess;
            public bool MoveMetaFileToOutput;
            public string Warning;
        }
    }
}
#endif
