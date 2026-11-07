using System;

namespace Game.Resource
{
    /// <summary>A scalar snapshot of one manager-owned independent acquisition, not native memory/refcounts.</summary>
    public sealed class ResourceLeaseDiagnostic
    {
        public string SessionId { get; }
        public long LeaseId { get; }
        public string Owner { get; }
        public long ScopeId { get; }
        public int SceneHandle { get; }
        public string SceneName { get; }
        public string Location { get; }
        public string AssetType { get; }
        public string State { get; }
        public string CreationStack { get; }

        internal ResourceLeaseDiagnostic(
            string sessionId,
            long leaseId,
            string owner,
            long scopeId,
            int sceneHandle,
            string sceneName,
            string location,
            string assetType,
            string state,
            string creationStack)
        {
            SessionId = sessionId;
            LeaseId = leaseId;
            Owner = owner;
            ScopeId = scopeId;
            SceneHandle = sceneHandle;
            SceneName = sceneName;
            Location = location;
            AssetType = assetType;
            State = state;
            CreationStack = creationStack;
        }
    }
}
