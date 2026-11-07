using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;
using UnityEngine;

internal static partial class Program
{
    private enum ResourceCodeCase
    {
        Success,
        MissingResources,
        CorruptDll,
        CorruptMetadata,
        MissingProvider,
        Cancelled,
        PackagedOnly
    }

    private static Task ReleasePairFallback() => RunReleasePair(null);

    private static async Task RunReleasePair(ResourceCodeCase? resourceCase)
    {
        string temp = Path.Combine(Path.GetTempPath(), "release-pair-" + Guid.NewGuid().ToString("N"));
        string oldPersistent = Application.persistentDataPath;
        string oldStreaming = Application.streamingAssetsPath;
        Application.persistentDataPath = temp;
        Application.streamingAssetsPath = Path.Combine(temp, "StreamingAssets");
        PreparedRelease prepared = null;
        try
        {
            using var rsa = RSA.Create(3072);
            var key = rsa.ExportParameters(false);
            var baseline = new ReleaseManifest
            {
                appId = "app",
                platform = "windows-x64",
                channel = "development",
                baselineId = "baseline",
                version = 1,
                assemblies = new[] { PairFile(new byte[] { 1 }) },
                aotMetadata = Array.Empty<ReleaseFile>(),
                content = new ReleaseContent { packageName = "DefaultPackage", version = "tables-1" }
            };
            if (resourceCase.HasValue)
            {
                baseline.content.codeInResources = true;
                var metadata = PairFile(new byte[] { 9 });
                metadata.assemblyName = "Core";
                metadata.fileName = "Core.dll.bytes";
                baseline.aotMetadata = new[] { metadata };
            }

            var update = JsonUtility.FromJson<ReleaseManifest>(JsonUtility.ToJson(baseline));
            update.version = 2;
            update.assemblies = new[] { PairFile(new byte[] { 2 }) };
            update.content.version = "tables-2";
            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(update));
            var envelope = new SignedEnvelope
            {
                keyId = "test",
                payload = Convert.ToBase64String(payload),
                signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            };
            var transport = new PairTransport(JsonUtility.ToJson(baseline), JsonUtility.ToJson(envelope));
            var settings = Settings();
            settings.appId = "app";
            settings.enableRemote = true;
            settings.remoteBaseUrl = "https://15000.top/game-updates/manifests/app/";
            settings.remoteFilesBaseUrl = "https://15000.top/game-updates/patches/app/";
            settings.scopeRemoteByBaseline = true;
            settings.trustedKeys = new[]
            {
                new TrustedKey
                {
                    keyId = "test",
                    modulus = Convert.ToBase64String(key.Modulus),
                    exponent = Convert.ToBase64String(key.Exponent)
                }
            };
            var versions = new List<string>();
            settings.prepareContentAsync = (content, token) =>
            {
                versions.Add(content.PackageVersion);
                bool fail = !resourceCase.HasValue || resourceCase == ResourceCodeCase.MissingResources;
                return content.IsPackaged || !fail ? Task.CompletedTask : Task.FromException(new IOException("Missing resource bundle"));
            };
            if (resourceCase.HasValue && resourceCase != ResourceCodeCase.MissingProvider)
            {
                settings.loadCodeAsync = (content, file, token) =>
                {
                    True(versions[versions.Count - 1] == content.PackageVersion);
                    True(content.CodeLocation(file) == "HotUpdate/baseline/" + file.fileName);
                    if (!content.IsPackaged && resourceCase == ResourceCodeCase.Cancelled)
                    {
                        return Task.FromCanceled<byte[]>(new CancellationToken(true));
                    }

                    bool metadata = file.assemblyName == "Core";
                    byte value = metadata ? (byte)9 : content.IsPackaged ? (byte)1 : (byte)2;
                    if (!content.IsPackaged && (metadata && resourceCase == ResourceCodeCase.CorruptMetadata
                        || !metadata && resourceCase == ResourceCodeCase.CorruptDll))
                    {
                        value = 7;
                    }

                    return Task.FromResult(new[] { value });
                };
            }

            if (resourceCase == ResourceCodeCase.MissingProvider)
            {
                await Throws<InvalidDataException>(new UpdateCoordinator(settings, _ => { }, transport).Prepare(CancellationToken.None));
                True(versions.Count == 0 && transport.DownloadUrl == null && transport.ByteReads == 0);
                return;
            }

            if (resourceCase == ResourceCodeCase.Cancelled)
            {
                await Throws<OperationCanceledException>(new UpdateCoordinator(settings, _ => { }, transport).Prepare(CancellationToken.None));
                True(versions.Count == 1 && transport.DownloadUrl == null && transport.ByteReads == 0);
                return;
            }

            prepared = await new UpdateCoordinator(settings, _ => { }, transport).Prepare(CancellationToken.None);
            if (resourceCase == ResourceCodeCase.Success || resourceCase == ResourceCodeCase.PackagedOnly)
            {
                True(string.Join(",", versions) == "tables-2");
                True(prepared.Id != null && prepared.Assemblies[0][0] == 2 && prepared.AotMetadata[0][0] == 9);
                True(transport.DownloadUrl == null && transport.ByteReads == 0);
                True(Directory.GetFiles(prepared.Directory, "*.dll.bytes").Length == 0);
                prepared.Cache.MarkHealthy(prepared.Id);
                string releaseId = prepared.Id;
                string cacheRoot = prepared.Cache.Root;
                prepared.Cache.Dispose();
                prepared = null;
                if (resourceCase == ResourceCodeCase.PackagedOnly)
                {
                    string interrupted = Path.Combine(cacheRoot, ".staging-preserve");
                    Directory.CreateDirectory(interrupted);
                    File.WriteAllText(Path.Combine(interrupted, "partial"), "unfinished online download");
                    File.WriteAllText(Path.Combine(cacheRoot, ".accepted-preserve"), "unfinished online receipt");
                    var before = PairCacheSnapshot(cacheRoot);
                    int remoteRequests = transport.ManifestReads;
                    settings.usePackagedReleaseOnly = true;
                    settings.Validate();
                    settings.appId = "another-app";
                    await Throws<InvalidDataException>(new UpdateCoordinator(settings, _ => { }, transport).Prepare(CancellationToken.None));
                    settings.appId = "app";
                    prepared = await new UpdateCoordinator(settings, _ => { }, transport).Prepare(CancellationToken.None);
                    True(prepared.Id == null && prepared.Cache == null && prepared.Manifest.version == 1);
                    using (var lease = new FileStream(Path.Combine(cacheRoot, ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        prepared = await new UpdateCoordinator(settings, _ => { }, transport).Prepare(CancellationToken.None);
                    }

                    True(prepared.Id == null && prepared.Cache == null && prepared.Directory == null);
                    True(prepared.Manifest.version == 1 && prepared.Manifest.content.version == "tables-1");
                    True(prepared.Assemblies[0][0] == 1 && prepared.AotMetadata[0][0] == 9);
                    True(transport.ManifestReads == remoteRequests && transport.DownloadUrl == null && transport.ByteReads == 0);
                    var after = PairCacheSnapshot(cacheRoot);
                    True(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string value) && value == pair.Value));

                    settings.usePackagedReleaseOnly = false;
                    transport.RemoteUnavailable = true;
                    prepared = await new UpdateCoordinator(settings, _ => { }, transport).Prepare(CancellationToken.None);
                    True(prepared.Id == releaseId && prepared.Manifest.version == 2 && prepared.Assemblies[0][0] == 2);
                    True(prepared.Manifest.content.version == "tables-2" && prepared.AotMetadata[0][0] == 9);
                    True(transport.ManifestReads > remoteRequests && !prepared.Cache.WasRejected(releaseId));
                    True(File.Exists(Path.Combine(prepared.Directory, "healthy")));
                    True(before["F:accepted.json"] == PairFileSnapshot(Path.Combine(cacheRoot, "accepted.json")));
                    prepared.Cache.MarkHealthy(prepared.Id, false);
                    True(string.Join(",", versions) == "tables-2,tables-1,tables-1,tables-2");
                    return;
                }

                settings.enableRemote = false;
                prepared = await new UpdateCoordinator(settings, _ => { }, transport).Prepare(CancellationToken.None);
                True(prepared.Id != null && prepared.Assemblies[0][0] == 2);
                True(transport.DownloadUrl == null && transport.ByteReads == 0);
                return;
            }

            True(string.Join(",", versions) == "tables-2,tables-1");
            True(prepared.Id == null && prepared.Manifest.content.version == "tables-1");
            True(prepared.Assemblies[0][0] == 1);
            True(prepared.Cache.WasRejected(ReleasePolicy.Sha256(payload)));
            if (resourceCase.HasValue)
            {
                True(transport.DownloadUrl == null && transport.ByteReads == 0);
                True(prepared.AotMetadata[0][0] == 9);
            }
            else
            {
                True(transport.DownloadUrl.StartsWith("https://15000.top/game-updates/patches/app/windows-x64/development/baseline/"));
            }
            True(transport.ManifestUrl == "https://15000.top/game-updates/manifests/app/windows-x64/development/baseline/release.json");
        }
        finally
        {
            prepared?.Cache?.Dispose();
            Application.persistentDataPath = oldPersistent;
            Application.streamingAssetsPath = oldStreaming;
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    private static async Task PackagedOnlySettings()
    {
        var settings = Settings();
        True(!settings.usePackagedReleaseOnly);
        settings.enableRemote = true;
        settings.remoteBaseUrl = "unused remote address";
        settings.remoteFilesBaseUrl = "unused files address";
        settings.trustedKeys = null;
        settings.usePackagedReleaseOnly = true;
        settings.Validate();
        settings.usePackagedReleaseOnly = false;
        await Throws<InvalidDataException>(Task.Run(settings.Validate));
        settings.usePackagedReleaseOnly = true;
        settings.appId = "";
        await Throws<InvalidDataException>(Task.Run(settings.Validate));
    }

    private static Dictionary<string, string> PairCacheSnapshot(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
        {
            result.Add("D:" + Path.GetRelativePath(root, directory), Directory.GetLastWriteTimeUtc(directory).Ticks.ToString());
        }

        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            result.Add("F:" + Path.GetRelativePath(root, file), PairFileSnapshot(file));
        }

        return result;
    }

    private static string PairFileSnapshot(string file)
    {
        return ReleasePolicy.Sha256(File.ReadAllBytes(file)) + ":" + File.GetLastWriteTimeUtc(file).Ticks;
    }

    private static ReleaseFile PairFile(byte[] bytes) => new ReleaseFile
    {
        assemblyName = "Game",
        fileName = "Game.dll.bytes",
        size = bytes.Length,
        sha256 = ReleasePolicy.Sha256(bytes)
    };

    private sealed class PairTransport : IUpdateTransport
    {
        private readonly string baseline;
        private readonly string update;
        internal string DownloadUrl;
        internal string ManifestUrl;
        internal int ByteReads;
        internal int ManifestReads;
        internal bool RemoteUnavailable;

        internal PairTransport(string baseline, string update)
        {
            this.baseline = baseline;
            this.update = update;
        }

        public Task<string> ReadText(string url, CancellationToken token)
        {
            if (url.EndsWith("builtin.json", StringComparison.Ordinal))
            {
                return Task.FromResult(baseline);
            }

            ManifestUrl = url;
            ManifestReads++;
            if (RemoteUnavailable)
            {
                return Task.FromException<string>(new IOException("Remote service unavailable"));
            }

            return Task.FromResult(update);
        }

        public Task<byte[]> ReadBytes(string url, long limit, CancellationToken token)
        {
            ByteReads++;
            return Task.FromResult(new byte[] { 1 });
        }

        public Task DownloadVerified(Uri url, string destination, ReleaseFile file, CancellationToken token)
        {
            DownloadUrl = url.AbsoluteUri;
            File.WriteAllBytes(destination, new byte[] { 2 });
            ReleasePolicy.VerifyFile(destination, file);
            return Task.CompletedTask;
        }
    }
}
