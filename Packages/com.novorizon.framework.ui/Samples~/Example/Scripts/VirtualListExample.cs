#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sample
{
    /// <summary>Attach alongside a configured ScrollView. Call Initialize after project resources are ready.</summary>
    public sealed class VirtualListExample : MonoBehaviour
    {
        [SerializeField] ScrollRect? scroll = null;
        [SerializeField] UIVirtualList? list = null;
        [SerializeField] GameObject? itemPrefab = null;

        public void Initialize()
        {
            if (scroll == null || list == null || itemPrefab == null)
                throw new System.InvalidOperationException("Assign ScrollRect, UIVirtualList and a row prefab.");
            list.Configure(scroll, itemPrefab, 64);
            list.SetItemCount(10_000, Bind);
        }

        static void Bind(GameObject row, int index)
        {
            // Business formatting may allocate; the virtual-list recycling itself does not.
            Text? label = row.GetComponentInChildren<Text>(true);
            if (label != null) label.text = index.ToString();
        }

        public void Clear() { if (list != null) list.Clear(); }
    }
}
