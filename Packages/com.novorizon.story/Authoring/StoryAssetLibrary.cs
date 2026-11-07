using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Framework.Story.Unity
{
    /// <summary>Small local stories can use inspector references without generated tables or a resource SDK.</summary>
    [CreateAssetMenu(menuName = "Novorizon/Story/Asset Library", fileName = "StoryAssets")]
    public sealed class StoryAssetLibrary : ScriptableObject, IStoryAssets
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            public UnityEngine.Object asset;
        }

        public Entry[] entries = Array.Empty<Entry>();

        public Task<StoryAssetLease<T>> LoadAsync<T>(string id, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Story asset ID is required.", nameof(id));
            }
            T selected = null;
            bool found = false;
            foreach (var entry in entries ?? Array.Empty<Entry>())
            {
                if (entry == null || !string.Equals(entry.id, id, StringComparison.Ordinal))
                {
                    continue;
                }
                if (found)
                {
                    throw new InvalidOperationException("Duplicate story asset ID: " + id);
                }
                found = true;
                selected = entry.asset as T;
            }
            if (!found || selected == null)
            {
                throw new InvalidOperationException("Story asset '" + id + "' is missing or is not a " + typeof(T).Name + ".");
            }
            // Library entries remain project-owned. This lease retains a reference but never unloads shared assets.
            return Task.FromResult(new StoryAssetLease<T>(selected));
        }
    }
}
