using System;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace Game.Framework.Localization.Unity
{
    /// <summary>提供本地化表数据资源的实现。</summary>
    [CreateAssetMenu(menuName = "Novorizon/Localization/Table", fileName = "LocalizationTable")]
    public sealed class LocalizationTableAsset : ScriptableObject
    {
        [SerializeField] private string[] languages = { "en", "zh-Hans" };
        [SerializeField] private LocalizationTableRow[] rows = Array.Empty<LocalizationTableRow>();
        private int contentVersion;

        public int ContentVersion => Volatile.Read(ref contentVersion);
        /// <summary>获取数据。</summary>
        public LocalizationTableData GetData() => new LocalizationTableData(languages, rows);
        /// <summary>创建目录。</summary>
        public LocalizationCatalog CreateCatalog() => GetData().CreateCatalog();

        /// <summary>Publish only validated copied authoring data, retaining the last valid asset on import failure.</summary>
        /// <remarks>设置数据。</remarks>
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

        /// <summary>Unity 在编辑器里验证序列化数据时执行本地校验。</summary>
        private void OnValidate()
        {
            Interlocked.Increment(ref contentVersion);
        }
    }
}
