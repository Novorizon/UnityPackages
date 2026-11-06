using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Game.Framework.Config.Editor
{
    /// <summary>只向项目 Library 缓存工具。生成到临时目录，全部成功后替换专用输出目录。</summary>
    public static class LubanExporter
    {
        public const string ToolVersion = "4.12.0";
        public const string ToolCommit = "0e1a6179a524e71cc503c20bebbca47cc59c7619";
        private const string Marker = ".novorizon-luban-output";
        public static async Task<string> EnsureToolAsync(string projectRoot, string dotnet = "dotnet")
        {
            projectRoot = Path.GetFullPath(projectRoot);
            string cache = Path.Combine(projectRoot, "Library/Novorizon/Luban", ToolVersion);
            string dll = Path.Combine(cache, "cli/Luban.dll");
            if (File.Exists(dll) && File.Exists(Path.Combine(cache, "commit")) && File.ReadAllText(Path.Combine(cache, "commit")) == ToolCommit)
            {
                return dll;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cache));
            using var cacheLock = new FileStream(cache + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            string stage = cache + "-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stage);
            try
            {
                await RunAsync("git", new[] { "clone", "--depth", "1", "--branch", "v" + ToolVersion, "https://github.com/focus-creative-games/luban.git", Path.Combine(stage, "source") }, projectRoot);
                string commit = await RunAsync("git", new[] { "rev-parse", "HEAD" }, Path.Combine(stage, "source"));
                if (commit.Trim() != ToolCommit)
                {
                    throw new InvalidOperationException("The Luban tag no longer matches the pinned source commit.");
                }

                await RunAsync(dotnet, new[] { "publish", Path.Combine(stage, "source/src/Luban/Luban.csproj"), "-c", "Release", "-o", Path.Combine(stage, "cli"), "-m:1", "--disable-build-servers", "-p:NuGetAudit=false" }, projectRoot);
                File.WriteAllText(Path.Combine(stage, "commit"), ToolCommit);
                // Source and build products stay inside Library; only the published CLI is kept in its cache.
                DeleteDirectory(Path.Combine(stage, "source"));
                if (Directory.Exists(cache))
                {
                    DeleteDirectory(cache);
                }

                Directory.Move(stage, cache);
                return dll;
            }
            finally
            {
                if (Directory.Exists(stage))
                {
                    DeleteDirectory(stage);
                }
            }
        }

        public static async Task GenerateAsync(string projectRoot, string configFile, string target, string codeOutput, string dataOutput, string locationPrefix, string dotnet, string lubanDll, string assemblyName = "Game.Config.Generated")
        {
            projectRoot = Path.GetFullPath(projectRoot);
            string code = OutputPath(projectRoot, codeOutput), data = OutputPath(projectRoot, dataOutput);
            if (IsInside(code, data) || IsInside(data, code))
            {
                throw new ArgumentException("Luban code and data outputs must be separate dedicated directories.");
            }

            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(assemblyName) || string.IsNullOrWhiteSpace(locationPrefix) || locationPrefix.Contains("\\") || locationPrefix.Contains("..") || locationPrefix.StartsWith("/"))
            {
                throw new ArgumentException("Target, assembly name and a relative asset location prefix are required.");
            }

            configFile = Path.GetFullPath(Path.Combine(projectRoot, configFile));
            if (IsInside(configFile, code) || IsInside(configFile, data))
            {
                throw new ArgumentException("Keep Luban source configuration outside its generated output directories.");
            }

            if (!File.Exists(configFile))
            {
                throw new FileNotFoundException("Luban configuration was not found.", configFile);
            }

            CheckOwnership(code);
            CheckOwnership(data);
            string cache = Path.Combine(projectRoot, "Library/Novorizon/Luban");
            Directory.CreateDirectory(cache);
            // One exporter per project prevents overlapping directory swaps, including batch-mode callers.
            using var exportLock = new FileStream(Path.Combine(cache, "export.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            string stage = Path.Combine(cache, "export-" + Guid.NewGuid().ToString("N"));
            string stagedCode = Path.Combine(stage, "code"), stagedData = Path.Combine(stage, "data");
            Directory.CreateDirectory(stagedCode);
            Directory.CreateDirectory(stagedData);
            bool published = false;
            try
            {
                await RunAsync(dotnet, new[] { Path.GetFullPath(lubanDll), "-t", target, "-c", "cs-bin", "-d", "bin", "--conf", configFile, "-x", "outputCodeDir=" + stagedCode, "-x", "outputDataDir=" + stagedData }, Path.GetDirectoryName(configFile));
                if (!Directory.EnumerateFiles(stagedCode, "*.cs", SearchOption.AllDirectories).Any())
                {
                    throw new InvalidOperationException("Luban succeeded without producing C# tables.");
                }

                File.WriteAllText(Path.Combine(stagedCode, assemblyName + ".asmdef"), "{\"name\":" + Json(assemblyName) + ",\"references\":[\"Luban.Runtime\"],\"autoReferenced\":true}");
                var files = Directory.GetFiles(stagedData, "*.bytes", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                var manifest = new StringBuilder("{\"version\":1,\"format\":\"bin\",\"files\":[");
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < files.Length; i++)
                {
                    string relative = files[i].Substring(stagedData.Length + 1).Replace('\\', '/');
                    string name = relative.Substring(0, relative.Length - ".bytes".Length);
                    if (!names.Add(name))
                    {
                        throw new InvalidOperationException("Duplicate Luban output: " + name);
                    }

                    if (i != 0)
                    {
                        manifest.Append(',');
                    }

                    manifest.Append("{\"name\":").Append(Json(name)).Append(",\"location\":").Append(Json(locationPrefix.TrimEnd('/') + "/" + name)).Append('}');
                }

                manifest.Append("]}");
                File.WriteAllText(Path.Combine(stagedData, "manifest.json"), manifest.ToString(), new UTF8Encoding(false));
                Publish(code, data, stagedCode, stagedData, stage);
                published = true;
            }
            finally
            {
                // A failed rollback must leave backups available for recovery.
                if (Directory.Exists(stage) && (published || !Directory.EnumerateDirectories(stage, "backup*").Any()))
                {
                    DeleteDirectory(stage);
                }
            }
        }

        private static void Publish(string code, string data, string stagedCode, string stagedData, string stage)
        {
            string[] destinations =
            {
                code,
                data
            }, sources =
            {
                stagedCode,
                stagedData
            };
            bool[] backedUp = new bool[2], installed = new bool[2];
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    CheckOwnership(destinations[i]);
                    PreserveMetadata(destinations[i], sources[i]);
                    File.WriteAllText(Path.Combine(sources[i], Marker), "Generated by Novorizon Luban. This entire directory is generator-owned.\n");
                    Directory.CreateDirectory(Path.GetDirectoryName(destinations[i]));
                    if (Directory.Exists(destinations[i]))
                    {
                        Directory.Move(destinations[i], Path.Combine(stage, "backup" + i));
                        backedUp[i] = true;
                    }

                    Directory.Move(sources[i], destinations[i]);
                    installed[i] = true;
                }
            }
            catch (Exception failure)
            {
                var errors = new List<Exception>
                {
                    failure
                };
                for (int i = 1; i >= 0; i--)
                {
                    try
                    {
                        if (installed[i])
                        {
                            DeleteDirectory(destinations[i]);
                        }

                        if (backedUp[i])
                        {
                            Directory.Move(Path.Combine(stage, "backup" + i), destinations[i]);
                        }
                    }
                    catch (Exception recoveryError)
                    {
                        errors.Add(recoveryError);
                    }
                }

                if (errors.Count > 1)
                {
                    throw new AggregateException("Luban output rollback failed; recover retained backups from " + stage, errors);
                }

                throw;
            }
        }

        private static void DeleteDirectory(string path)
        {
            // Git packs can be read-only on Windows. These are tool-owned staging/cache paths.
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            Directory.Delete(path, true);
        }

        private static void PreserveMetadata(string oldDirectory, string newDirectory)
        {
            if (!Directory.Exists(oldDirectory))
            {
                return;
            }

            foreach (var meta in Directory.EnumerateFiles(oldDirectory, "*.meta", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(newDirectory, meta.Substring(oldDirectory.Length + 1));
                string asset = destination.Substring(0, destination.Length - 5);
                if (File.Exists(asset) || Directory.Exists(asset))
                {
                    File.Copy(meta, destination, true);
                }
            }
        }

        private static void CheckOwnership(string directory)
        {
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any() && !File.Exists(Path.Combine(directory, Marker)))
            {
                throw new InvalidOperationException("Choose an empty dedicated output directory; refusing to replace user files: " + directory);
            }
        }

        private static string OutputPath(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                throw new ArgumentException("An output directory is required.");
            }

            string result = Path.GetFullPath(Path.Combine(root, relative)).TrimEnd(Path.DirectorySeparatorChar);
            string assets = Path.Combine(root, "Assets");
            if (!IsInside(result, assets) || string.Equals(result, assets, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Generated outputs must be dedicated subdirectories of Assets.");
            }

            return result;
        }

        private static bool IsInside(string child, string parent) => string.Equals(child, parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        private static string Json(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        public static async Task<string> RunAsync(string executable, IEnumerable<string> args, string workingDirectory)
        {
            var info = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = string.Join(" ", args.Select(Quote))
            };
            using var process = new Process
            {
                StartInfo = info
            };
            try
            {
                process.Start();
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("Cannot start " + executable + ". Install Git and the .NET 8 SDK, then restart Unity/Hub, or set their executable paths.", error);
            }

            Task<string> output = process.StandardOutput.ReadToEndAsync(), errors = process.StandardError.ReadToEndAsync();
            await Task.Run(() => process.WaitForExit());
            string log = await output + "\n" + await errors;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(executable + " failed (" + process.ExitCode + "):\n" + log);
            }

            return log;
        }

        // ProcessStartInfo.Arguments uses the Windows argv quoting rules on Unity's supported editor runtimes.
        private static string Quote(string argument)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }

                if (c == '"')
                {
                    result.Append('\\', slashes * 2 + 1).Append(c);
                }
                else
                {
                    result.Append('\\', slashes).Append(c);
                }

                slashes = 0;
            }

            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
    }
}
