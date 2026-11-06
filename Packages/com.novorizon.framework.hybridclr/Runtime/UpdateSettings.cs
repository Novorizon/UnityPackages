using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Framework.HotUpdate
{
    [Serializable]
    public sealed class UpdateSettings
    {
        public string hotUpdateRoot = "Assets/HotUpdate";
        public string entryAssembly = "Game.HotUpdate";
        public string entryType = "Game.HotUpdate.HotGame";
        public bool enableRemote;
        public string appId = Application.identifier;
        public string channel = "development";
        public string remoteBaseUrl = "";
        public string remoteFilesBaseUrl = "";
        public bool scopeRemoteByBaseline;
        public bool loadFromCompiledAssemblies;
        public ReleaseContent content;
        [NonSerialized]
        public Func<ReleaseContentContext, CancellationToken, Task> prepareContentAsync;
        public int requestTimeoutSeconds = 20;
        public int requestAttempts = 2;
        public int maxCachedReleases = 8;
        public int prepareTimeoutSeconds = 120;
        public int startupTimeoutSeconds = 60;
        public TrustedKey[] trustedKeys = Array.Empty<TrustedKey>();
        public void Validate()
        {
            ReleasePolicy.Require(!string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(channel), "Application ID and channel are required.");
            ReleasePolicy.Require(System.Text.RegularExpressions.Regex.IsMatch(entryAssembly ?? "", @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,100}\z") && !entryAssembly.Contains("..") && System.Text.RegularExpressions.Regex.IsMatch(entryType ?? "", @"\A[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+\z"), "Invalid hot update entry.");
            ReleasePolicy.Require(hotUpdateRoot != null && hotUpdateRoot.StartsWith("Assets/", StringComparison.Ordinal) && !hotUpdateRoot.Contains("\\") && !hotUpdateRoot.Split('/').Any(p => p == ".." || p == "." || p == ""), "Hot update root must be a folder beneath Assets.");
            ReleasePolicy.Require(requestTimeoutSeconds >= 5 && requestTimeoutSeconds <= 120 && requestAttempts >= 1 && requestAttempts <= 3 && maxCachedReleases >= 3 && maxCachedReleases <= 16 && prepareTimeoutSeconds >= 10 && prepareTimeoutSeconds <= 600 && startupTimeoutSeconds >= 5 && startupTimeoutSeconds <= 300, "Invalid update limits.");
            if (!enableRemote)
            {
                return;
            }

            ReleasePolicy.HttpsDirectory(remoteBaseUrl);
            if (!string.IsNullOrWhiteSpace(remoteFilesBaseUrl))
            {
                ReleasePolicy.HttpsDirectory(remoteFilesBaseUrl);
            }
            ReleasePolicy.Require(trustedKeys != null && trustedKeys.Length > 0, "Remote updates need trusted public keys.");
            var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var key in trustedKeys)
            {
                ReleasePolicy.Require(key != null && !string.IsNullOrWhiteSpace(key.keyId) && ids.Add(key.keyId) && Convert.FromBase64String(key.modulus).Length >= 384 && Convert.FromBase64String(key.exponent).Length > 0, "Invalid/duplicate public key.");
            }
        }
    }
}
