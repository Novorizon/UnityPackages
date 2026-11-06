using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Game.Framework.HotUpdate
{
    [Serializable] public sealed class ReleaseFile
    {
        public string assemblyName;
        public string fileName;
        public long size;
        public string sha256;
    }

    [Serializable] public sealed class ReleaseManifest
    {
        public int schema = 1;
        public string appId;
        public string platform;
        public string channel;
        public string baselineId;
        public int contractVersion = 1;
        public long version;
        public ReleaseFile[] assemblies;
        public ReleaseFile[] aotMetadata;
        public ReleaseContent content;
    }

    [Serializable]
    public sealed class ReleaseContent
    {
        public string packageName;
        public string version;
        public bool codeInResources;
    }

    /// <summary>从已验证发行复制的资源绑定，在加载任何热更程序集之前交给项目准备。</summary>
    public sealed class ReleaseContentContext
    {
        public string Platform { get; }
        public string PackageName { get; }
        public string PackageVersion { get; }
        public bool IsPackaged { get; }
        public string BaselineId { get; }

        public ReleaseContentContext(string platform, ReleaseContent content, bool isPackaged, string baselineId = null)
        {
            Platform = platform;
            PackageName = content?.packageName;
            PackageVersion = content?.version;
            IsPackaged = isPackaged;
            BaselineId = baselineId;
        }

        public string CodeLocation(ReleaseFile file)
        {
            ReleasePolicy.Require(!string.IsNullOrEmpty(BaselineId), "Code resources require a player baseline.");
            return "HotUpdate/" + BaselineId + "/" + file.fileName;
        }
    }

    [Serializable] public sealed class SignedEnvelope
    {
        public string keyId;
        public string payload;
        public string signature;
    }

    [Serializable] public sealed class TrustedKey
    {
        public string keyId;
        public string modulus;
        public string exponent;
    }

    public static class ReleasePolicy
    {
        public const int MaxManifestBytes = 64 * 1024;
        public const long MaxFileBytes = 32 * 1024 * 1024;
        public const long MaxReleaseBytes = 128 * 1024 * 1024;
        private static readonly Regex SafeName = new Regex(@"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,100}\z");
        private static readonly Regex Hash = new Regex(@"\A[0-9a-f]{64}\z");

        public static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }

        public static bool IsHash(string value) => value != null && Hash.IsMatch(value);

        public static void Validate(ReleaseManifest manifest)
        {
            Require(manifest != null && manifest.schema == 1, "Unsupported release schema.");
            foreach (string id in new[] { manifest.appId, manifest.platform, manifest.channel, manifest.baselineId })
                Require(id != null && SafeName.IsMatch(id) && !id.Contains(".."), "Invalid release identity.");
            Require(manifest.version > 0 && manifest.contractVersion == 1, "Invalid release/contract version.");
            Require(manifest.assemblies != null && manifest.assemblies.Length > 0 && manifest.assemblies.Length <= 16,
                "Expected 1-16 hot update assemblies.");
            Require(manifest.aotMetadata != null && manifest.aotMetadata.Length <= 16, "Invalid AOT metadata list.");
            if (manifest.content != null)
            {
                foreach (string value in new[] { manifest.content.packageName, manifest.content.version })
                {
                    Require(value != null && SafeName.IsMatch(value) && !value.Contains(".."), "Invalid content binding.");
                }
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var file in manifest.assemblies.Concat(manifest.aotMetadata))
            {
                Require(file != null && file.assemblyName != null && SafeName.IsMatch(file.assemblyName)
                    && !file.assemblyName.Contains(".."), "Invalid assembly name.");
                Require(file.fileName == file.assemblyName + ".dll.bytes", "Invalid file path.");
                Require(names.Add(file.fileName), "Duplicate release file.");
                Require(file.size > 0 && file.size <= MaxFileBytes && IsHash(file.sha256), "Invalid file size/hash.");
                total += file.size;
            }
            Require(total <= MaxReleaseBytes, "Release exceeds size budget.");
        }

        // An update may change hot code, but cannot replace the AOT baseline or its assembly topology.
        public static void MatchBaseline(ReleaseManifest update, ReleaseManifest baseline)
        {
            Validate(update);
            Validate(baseline);
            Require(update.appId == baseline.appId && update.platform == baseline.platform
                && update.channel == baseline.channel && update.baselineId == baseline.baselineId
                && update.contractVersion == baseline.contractVersion, "Update does not match this player baseline.");
            Require(update.assemblies.Select(f => f.assemblyName).SequenceEqual(
                baseline.assemblies.Select(f => f.assemblyName)), "Assembly dependency order/topology changed.");
            Require((update.content == null) == (baseline.content == null)
                && (update.content == null || update.content.packageName == baseline.content.packageName), "Content package binding changed.");
            Require(update.content == null || update.content.codeInResources == baseline.content.codeInResources,
                "Code delivery mode changed; create a new player baseline.");
            Require(update.aotMetadata.Length == baseline.aotMetadata.Length, "AOT metadata list changed.");
            for (int i = 0; i < baseline.aotMetadata.Length; i++)
            {
                var a = update.aotMetadata[i];
                var b = baseline.aotMetadata[i];
                Require(a.fileName == b.fileName && a.sha256 == b.sha256 && a.size == b.size,
                    "AOT metadata must remain pinned to the shipped baseline.");
            }
        }

        public static string Sha256(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(bytes));
        }

        public static string Sha256File(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(stream));
        }

        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

        public static void VerifyFile(string path, ReleaseFile file)
        {
            var info = new FileInfo(path);
            Require(info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0
                && info.Length == file.size, "Missing/invalid file size: " + file.fileName);
            Require(Sha256File(path) == file.sha256, "SHA256 mismatch: " + file.fileName);
        }

        public static byte[] ReadVerifiedFile(string path, ReleaseFile file)
        {
            Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Code file must not be a link.");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Require(stream.Length == file.size && file.size <= MaxFileBytes, "Unexpected code file length.");
                var bytes = new byte[(int)file.size];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    Require(read > 0, "Code file was truncated.");
                    offset += read;
                }
                Require(Sha256(bytes) == file.sha256, "Code file hash mismatch.");
                return bytes;
            }
        }

        public static byte[] VerifyEnvelope(SignedEnvelope envelope, IEnumerable<TrustedKey> trustedKeys)
        {
            Require(envelope != null && envelope.payload != null && envelope.signature != null
                && envelope.payload.Length <= MaxManifestBytes && envelope.signature.Length <= 2048,
                "Invalid signed envelope.");
            var key = trustedKeys?.SingleOrDefault(k => k != null && k.keyId == envelope.keyId);
            Require(key != null, "Untrusted signing key.");
            byte[] payload = Convert.FromBase64String(envelope.payload);
            Require(payload.Length > 0 && payload.Length <= MaxManifestBytes, "Invalid payload size.");
            var parameters = new RSAParameters
            {
                Modulus = Convert.FromBase64String(key.modulus),
                Exponent = Convert.FromBase64String(key.exponent)
            };
            Require(parameters.Modulus.Length >= 384, "RSA key must be at least 3072 bits.");
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(parameters);
                Require(rsa.VerifyData(payload, Convert.FromBase64String(envelope.signature),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), "Manifest signature verification failed.");
            }
            // Verify exact payload bytes before parsing; no JSON canonicalization assumptions.
            return payload;
        }

        public static string ReadBoundedText(string path)
        {
            Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Manifest must not be a link.");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Require(stream.Length > 0 && stream.Length <= MaxManifestBytes, "Manifest exceeds size limit or is empty.");
                var bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    Require(read > 0, "Manifest was truncated while reading.");
                    offset += read;
                }
                Require(stream.ReadByte() == -1, "Manifest changed while reading.");
                string text = new UTF8Encoding(false, true).GetString(bytes);
                return text.Length > 0 && text[0] == '\uFEFF' ? text.Substring(1) : text;
            }
        }

        public static Uri HttpsDirectory(string value)
        {
            Require(Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
                && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath.EndsWith("/"),
                "Update URL must be an HTTPS directory without credentials, query or fragment.");
            return uri;
        }
    }
}
