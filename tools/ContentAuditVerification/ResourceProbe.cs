using System;
using System.Collections.Generic;
using Game.Framework.Diagnostics.Editor;

// This finite provider exercises the real optional-package reflection boundary. It does
// not simulate native asset loading, retain Unity objects, or claim resource-memory data.
namespace Game.Resource
{
    public sealed class ResourceManager
    {
        private static ResourceManager current;
        private readonly ResourceRecord[] records;
        public int OwnedAssetCount => records.Length;

        private ResourceManager(DiagnosticLease[] leases)
        {
            records = new ResourceRecord[leases.Length];
            for (int index = 0; index < leases.Length; index++)
            {
                records[index] = new ResourceRecord(leases[index]);
            }
        }

        public static void Publish(DiagnosticLease[] leases)
        {
            current = new ResourceManager(leases);
        }

        public static bool TryGetInstance(out ResourceManager instance)
        {
            instance = current;
            return instance != null;
        }

        public IReadOnlyList<ResourceRecord> CaptureLeaseDiagnostics() => records;
    }

    public sealed class ResourceRecord
    {
        private readonly DiagnosticLease value;
        public string SessionId => value.sessionId;
        public long LeaseId => value.leaseId;
        public string Owner => value.owner;
        public long ScopeId => value.scopeId;
        public int SceneHandle => value.sceneHandle;
        public string SceneName => value.sceneName;
        public string Location => value.location;
        public string AssetType => value.assetType;
        public string State => value.state;
        public string CreationStack => value.creationStack;

        internal ResourceRecord(DiagnosticLease value)
        {
            this.value = value;
        }
    }
}
