using System;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace Game.Framework.Localization.Unity
{
    [CreateAssetMenu(menuName = "Novorizon/Localization/Table", fileName = "LocalizationTable")]
    public sealed class LocalizationTableAsset : ScriptableObject
    {
        [SerializeField] private string[] languages = { "en", "zh-Hans" };
        [SerializeField] private LocalizationTableRow[] rows = Array.Empty<LocalizationTableRow>();
        private int contentVersion;

        public int ContentVersion => Volatile.Read(ref contentVersion);
        public LocalizationTableData GetData() => new LocalizationTableData(languages, rows);
        public LocalizationCatalog CreateCatalog() => GetData().CreateCatalog();

        /// <summary>Publish only validated copied authoring data, retaining the last valid asset on import failure.</summary>
        public void SetData(LocalizationTableData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            string[] nextLanguages = data.Languages.ToArray();
            LocalizationTableRow[] nextRows = data.CopyRows();
            languages = nextLanguages;
            rows = nextRows;
            Interlocked.Increment(ref contentVersion);
        }

        private void OnValidate()
        {
            Interlocked.Increment(ref contentVersion);
        }
    }
}
