using System;
using System.Globalization;
using UnityEngine;

namespace Game.Framework.Localization.Unity
{
    /// <summary>本地化对应的设置。保存本模块使用的配置。</summary>
    [CreateAssetMenu(menuName = "Novorizon/Localization/Settings", fileName = "LocalizationSettings")]
    public sealed class LocalizationSettings : ScriptableObject
    {
        public LocalizationTableAsset table;
        public string defaultLanguage = "en";
        public string fallbackLanguage = "en";
        public bool useSystemLanguage = true;

        /// <summary>创建目录。</summary>
        public LocalizationCatalog CreateCatalog()
        {
            if (table == null)
            {
                throw new InvalidOperationException("Assign a Localization Table in LocalizationSettings.");
            }
            var catalog = table.CreateCatalog();
            if (!catalog.ContainsLanguage(defaultLanguage) || !catalog.ContainsLanguage(fallbackLanguage))
            {
                throw new InvalidOperationException("Default and fallback languages must be authored table columns.");
            }
            return catalog;
        }

        /// <summary>解析语言。</summary>
        public string ResolveLanguage(string requestedLanguage)
        {
            return ResolveLanguage(CreateCatalog(), requestedLanguage);
        }

        /// <summary>解析语言。</summary>
        internal string ResolveLanguage(LocalizationCatalog catalog, string requestedLanguage)
        {
            if (!string.IsNullOrWhiteSpace(requestedLanguage))
            {
                var culture = CultureInfo.GetCultureInfo(requestedLanguage);
                if (culture.TwoLetterISOLanguageName == "zh")
                {
                    bool traditional = culture.Name.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        culture.Name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) ||
                        culture.Name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) ||
                        culture.Name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase);
                    string script = traditional ? "zh-Hant" : "zh-Hans";
                    if (!catalog.ContainsLanguage(culture.Name) && catalog.ContainsLanguage(script))
                    {
                        return script;
                    }
                }
                while (culture.Name.Length != 0)
                {
                    if (catalog.ContainsLanguage(culture.Name))
                    {
                        return culture.Name;
                    }
                    culture = culture.Parent;
                }
            }
            return CultureInfo.GetCultureInfo(defaultLanguage).Name;
        }
    }
}
