using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;
using UnityEngine;

namespace Game.Framework.HotUpdate
{
    internal sealed class PreparedRelease
    {
        public ReleaseManifest Manifest;
        public string Id;
        public string Directory;
        public byte[][] Assemblies;
        public byte[][] AotMetadata;
        public ReleaseCache Cache;
        public long AcceptedVersion;
    }

    internal sealed class UpdateCoordinator
    {
        private readonly UpdateSettings settings;
        private readonly IUpdateTransport transport;
        private readonly Action<string> log;
        private readonly string builtinRoot;
        private ReleaseManifest baseline;
        private ReleaseCache cache;

        public UpdateCoordinator(UpdateSettings settings, Action<string> log, IUpdateTransport transport = null)
        {
            this.settings = settings;
            this.log = log;
            this.transport = transport ?? new UpdateTransport(settings);
            builtinRoot = Application.streamingAssetsPath.TrimEnd('/') + "/HotUpdate";
        }

        private string BuiltinUrl(string fileName)
        {
            string path = builtinRoot + "/" + fileName;
            return path.Contains("://") ? path : new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }

        private ReleaseManifest Authenticate(string text, out string id)
        {
            ReleasePolicy.Require(Encoding.UTF8.GetByteCount(text) <= ReleasePolicy.MaxManifestBytes, "Oversized envelope.");
            var envelope = JsonUtility.FromJson<SignedEnvelope>(text);
            byte[] payload = ReleasePolicy.VerifyEnvelope(envelope, settings.trustedKeys);
            var manifest = JsonUtility.FromJson<ReleaseManifest>(Encoding.UTF8.GetString(payload));
            ReleasePolicy.MatchBaseline(manifest, baseline);
            id = ReleasePolicy.Sha256(payload);
            return manifest;
        }

        public async Task<PreparedRelease> Prepare(CancellationToken token)
        {
            try
            {
                var release = await PrepareInternal(token);
                try
                {
                    await PrepareContent(release, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception error) when (release.Id != null)
                {
                    cache.Reject(release.Id, "Content preparation failed: " + error.Message);
                    log("Content preparation failed; selecting packaged code and resources. " + error.Message);
                    release = await PreparePackaged(token);
                    release.Cache = cache;
                    await PrepareContent(release, token);
                }

                if (release.Id != null)
                {
                    try
                    {
                        cache.BeginBoot(release.Id);
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                    {
                        cache.Dispose();
                        cache = null;
                        log("Cannot track update health; using packaged content. " + error.Message);
                        release = await PreparePackaged(token);
                        await PrepareContent(release, token);
                    }
                }

                return release;
            }
            catch
            {
                cache?.Dispose();
                throw;
            }
        }

        private async Task PrepareContent(PreparedRelease release, CancellationToken token)
        {
            var context = new ReleaseContentContext(
                release.Manifest.platform,
                release.Manifest.content,
                release.Id == null,
                release.Manifest.baselineId);
            if (settings.prepareContentAsync != null)
            {
                await settings.prepareContentAsync(context, token);
            }

            if (settings.loadCodeAsync != null)
            {
                release.Assemblies = new byte[release.Manifest.assemblies.Length][];
                for (int i = 0; i < release.Assemblies.Length; i++)
                {
                    release.Assemblies[i] = await ReadResourceCode(context, release.Manifest.assemblies[i], token);
                }

                release.AotMetadata = new byte[baseline.aotMetadata.Length][];
                for (int i = 0; i < release.AotMetadata.Length; i++)
                {
                    release.AotMetadata[i] = await ReadResourceCode(context, baseline.aotMetadata[i], token);
                }
            }
        }

        private async Task<byte[]> ReadResourceCode(ReleaseContentContext context, ReleaseFile file, CancellationToken token)
        {
            byte[] bytes = await settings.loadCodeAsync(context, file, token);
            token.ThrowIfCancellationRequested();
            ReleasePolicy.Require(bytes != null && bytes.LongLength == file.size && ReleasePolicy.Sha256(bytes) == file.sha256,
                "Code resource failed verification: " + file.fileName);
            return bytes;
        }

        private async Task<PreparedRelease> PrepareInternal(CancellationToken token)
        {
            baseline = JsonUtility.FromJson<ReleaseManifest>(await transport.ReadText(BuiltinUrl("builtin.json"), token));
            ReleasePolicy.Validate(baseline);
            ReleasePolicy.Require(baseline.content?.codeInResources != true || settings.loadCodeAsync != null,
                "This player requires a resource code provider; standalone DLL downloads are disabled.");
            ReleasePolicy.Require(baseline.appId == settings.appId && baseline.channel == settings.channel
                && baseline.platform == PlatformId(), "Packaged baseline does not match application configuration.");
            try
            {
                cache = new ReleaseCache(Path.Combine(Application.persistentDataPath, "HotUpdate", baseline.platform,
                    baseline.channel, baseline.baselineId));
                cache.AcquireLease();
                cache.CleanInterruptedDownloads();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                cache?.Dispose();
                cache = null;
                log("Cache unavailable; using packaged content. " + exception.Message);
                return await PreparePackaged(token);
            }
            var candidates = new List<PreparedRelease>();
            var healthyReceipts = new List<PreparedRelease>();
            long highestHealthyVersion = baseline.version;
            string acceptedId = null;
            foreach (string receipt in new[] { "accepted.json", "accepted.json.bak" })
            {
                string path = Path.Combine(cache.Root, receipt);
                if (!File.Exists(path)) continue;
                try
                {
                    var accepted = Authenticate(ReleasePolicy.ReadBoundedText(path), out var id);
                    if (accepted.version > highestHealthyVersion)
                    { highestHealthyVersion = accepted.version; acceptedId = id; }
                }
                catch (Exception exception) { log("Ignored invalid acceptance receipt: " + exception.Message); }
            }
            foreach (string directory in cache.EnumerateReleases())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var manifest = Authenticate(ReleasePolicy.ReadBoundedText(Path.Combine(directory, "release.json")), out var id);
                    ReleasePolicy.Require(Path.GetFileName(directory) == id, "Cache content ID mismatch.");
                    // Keep the accepted version floor even if its DLL has subsequently been damaged.
                    if (cache.IsHealthy(id))
                    {
                        if (manifest.version > highestHealthyVersion)
                        { highestHealthyVersion = manifest.version; acceptedId = id; }
                        healthyReceipts.Add(new PreparedRelease { Manifest = manifest, Id = id });
                    }
                    if (cache.RecoverInterruptedBoot(id) || manifest.version < baseline.version)
                    {
                        healthyReceipts.RemoveAll(c => c.Id == id);
                        cache.DeleteRelease(id);
                        continue;
                    }
                    if (settings.loadCodeAsync == null)
                    {
                        await Task.Run(() => VerifyDirectory(directory, manifest), token);
                    }
                    candidates.Add(new PreparedRelease { Manifest = manifest, Id = id, Directory = directory, Cache = cache });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    log("Ignored invalid cache: " + exception.Message);
                    string id = Path.GetFileName(directory);
                    healthyReceipts.RemoveAll(c => c.Id == id);
                    try { cache.DeleteRelease(id); }
                    catch (Exception cleanupError) { log("Could not remove damaged cache: " + cleanupError.Message); }
                }
            }
            // Keep the newest healthy receipt even if damaged, plus two verified fallback releases.
            var keep = new HashSet<string>(candidates.OrderByDescending(c => c.Manifest.version).Take(2).Select(c => c.Id));
            var newestReceipt = healthyReceipts.OrderByDescending(c => c.Manifest.version).FirstOrDefault();
            if (newestReceipt != null) keep.Add(newestReceipt.Id);
            foreach (var old in healthyReceipts.Where(c => !keep.Contains(c.Id)))
            {
                try { cache.DeleteRelease(old.Id); }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                { log("Could not prune old cache: " + exception.Message); }
                candidates.RemoveAll(c => c.Id == old.Id);
            }
            if (settings.enableRemote)
            {
                try
                {
                    var remote = await FetchUpdate(highestHealthyVersion, acceptedId, token);
                    if (remote != null && candidates.All(c => c.Id != remote.Id)) candidates.Add(remote);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { log("Update unavailable; using verified local content. " + exception.Message); }
            }
            PreparedRelease selected = candidates.OrderByDescending(c => c.Manifest.version).FirstOrDefault()
                ?? new PreparedRelease { Manifest = baseline, Cache = cache };
            if (settings.loadCodeAsync == null)
            {
                // Materialize and hash all code bytes BEFORE any Assembly.Load to close file replacement races.
                try
                {
                    selected.Assemblies = await ReadAssemblies(selected, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (selected.Id != null)
                {
                    cache.Reject(selected.Id, exception.Message);
                    log("Cached content changed during startup; using packaged release.");
                    selected = new PreparedRelease { Manifest = baseline, Cache = cache };
                    selected.Assemblies = await ReadAssemblies(selected, token);
                }
                selected.AotMetadata = new byte[baseline.aotMetadata.Length][];
                for (int i = 0; i < baseline.aotMetadata.Length; i++)
                {
                    selected.AotMetadata[i] = await ReadBuiltinVerified(baseline.aotMetadata[i], token);
                }
            }

            selected.AcceptedVersion = highestHealthyVersion;
            log("Selected release " + selected.Manifest.version + " (" + (selected.Id == null ? "packaged" : "verified cache") + ").");
            return selected;
        }

        private async Task<PreparedRelease> PreparePackaged(CancellationToken token)
        {
            var release = new PreparedRelease { Manifest = baseline, AcceptedVersion = baseline.version };
            if (settings.loadCodeAsync == null)
            {
                release.Assemblies = await ReadAssemblies(release, token);
                release.AotMetadata = new byte[baseline.aotMetadata.Length][];
                for (int i = 0; i < release.AotMetadata.Length; i++)
                {
                    release.AotMetadata[i] = await ReadBuiltinVerified(baseline.aotMetadata[i], token);
                }
            }
            return release;
        }

        private async Task<byte[][]> ReadAssemblies(PreparedRelease release, CancellationToken token)
        {
            var result = new byte[release.Manifest.assemblies.Length][];
            for (int i = 0; i < result.Length; i++)
            {
                var file = release.Manifest.assemblies[i];
                byte[] bytes = release.Directory == null ? await ReadBuiltinVerified(file, token)
                    : await Task.Run(() => ReleasePolicy.ReadVerifiedFile(Path.Combine(release.Directory, file.fileName), file), token);
                ReleasePolicy.Require(bytes.LongLength == file.size && ReleasePolicy.Sha256(bytes) == file.sha256,
                    "Code changed after verification: " + file.fileName);
                result[i] = bytes;
            }
            return result;
        }

        private async Task<byte[]> ReadBuiltinVerified(ReleaseFile file, CancellationToken token)
        {
            byte[] bytes = await transport.ReadBytes(BuiltinUrl(file.fileName), file.size, token);
            ReleasePolicy.Require(bytes.LongLength == file.size && ReleasePolicy.Sha256(bytes) == file.sha256,
                "Packaged content failed verification: " + file.fileName);
            return bytes;
        }

        private static void VerifyDirectory(string directory, ReleaseManifest manifest)
        {
            foreach (var file in manifest.assemblies) ReleasePolicy.VerifyFile(Path.Combine(directory, file.fileName), file);
        }

        private async Task<PreparedRelease> FetchUpdate(long floor, string acceptedId, CancellationToken token)
        {
            Uri root = ReleasePolicy.HttpsDirectory(settings.remoteBaseUrl);
            if (settings.scopeRemoteByBaseline)
            {
                root = new Uri(root, baseline.platform + "/" + baseline.channel + "/" + baseline.baselineId + "/");
            }
            string text = null;
            for (int i = 0; i < settings.requestAttempts; i++)
            {
                try { text = await transport.ReadText(new Uri(root, "release.json").AbsoluteUri, token); break; }
                catch (OperationCanceledException) { throw; }
                catch when (i + 1 < settings.requestAttempts) { await Task.Delay(500 * (i + 1), token); }
            }
            var manifest = Authenticate(text, out var id);
            if (manifest.version < floor || (manifest.version == floor && id != acceptedId)) return null;
            if (cache.WasRejected(id)) { log("Skipped previously rejected release."); return null; }
            if (Directory.Exists(cache.ReleasePath(id)))
            {
                if (settings.loadCodeAsync == null)
                {
                    await Task.Run(() => VerifyDirectory(cache.ReleasePath(id), manifest), token);
                }
                return new PreparedRelease { Manifest = manifest, Id = id, Directory = cache.ReleasePath(id), Cache = cache };
            }
            // Bound disk growth; a full cache requires operator intervention rather than unbounded writes.
            ReleasePolicy.Require(cache.EnumerateReleases().Length < settings.maxCachedReleases,
                "Release cache is full. Existing verified version remains available.");
            string stage = cache.BeginStage();
            try
            {
                // Files live under the payload hash, so replacing release.json cannot mix release contents.
                Uri filesRoot = string.IsNullOrWhiteSpace(settings.remoteFilesBaseUrl)
                    ? root : ReleasePolicy.HttpsDirectory(settings.remoteFilesBaseUrl);
                if (settings.scopeRemoteByBaseline && !string.IsNullOrWhiteSpace(settings.remoteFilesBaseUrl))
                {
                    filesRoot = new Uri(filesRoot, baseline.platform + "/" + baseline.channel + "/" + baseline.baselineId + "/");
                }

                var releaseRoot = new Uri(filesRoot, id + "/");
                if (settings.loadCodeAsync == null)
                {
                    foreach (var file in manifest.assemblies)
                    {
                        await transport.DownloadVerified(new Uri(releaseRoot, file.fileName), Path.Combine(stage, file.fileName), file, token);
                    }
                }
                return new PreparedRelease { Manifest = manifest, Id = id, Directory = cache.Commit(stage, id, text), Cache = cache };
            }
            finally
            {
                try { cache.DiscardStage(stage); }
                catch (Exception exception) { log("Could not remove interrupted download: " + exception.Message); }
            }
        }

        internal static string PlatformId()
        {
#if UNITY_ANDROID
            return "android-arm64";
#elif UNITY_IOS
            return "ios-arm64";
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return "windows-x64";
#else
            throw new PlatformNotSupportedException("Supported release targets: Windows x64, Android ARM64, iOS ARM64.");
#endif
        }
    }
}
