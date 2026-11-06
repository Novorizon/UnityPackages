using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;
using UnityEngine;

internal static partial class Program
{
    private static async Task ReleasePairFallback()
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
                return content.IsPackaged ? Task.CompletedTask : Task.FromException(new IOException("Missing resource bundle"));
            };
            prepared = await new UpdateCoordinator(settings, _ => { }, transport).Prepare(CancellationToken.None);
            True(string.Join(",", versions) == "tables-2,tables-1");
            True(prepared.Id == null && prepared.Manifest.content.version == "tables-1");
            True(prepared.Assemblies[0][0] == 1);
            True(prepared.Cache.WasRejected(ReleasePolicy.Sha256(payload)));
            True(transport.DownloadUrl.StartsWith("https://15000.top/game-updates/patches/app/windows-x64/development/baseline/"));
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
            return Task.FromResult(update);
        }

        public Task<byte[]> ReadBytes(string url, long limit, CancellationToken token) => Task.FromResult(new byte[] { 1 });

        public Task DownloadVerified(Uri url, string destination, ReleaseFile file, CancellationToken token)
        {
            DownloadUrl = url.AbsoluteUri;
            File.WriteAllBytes(destination, new byte[] { 2 });
            ReleasePolicy.VerifyFile(destination, file);
            return Task.CompletedTask;
        }
    }
}
