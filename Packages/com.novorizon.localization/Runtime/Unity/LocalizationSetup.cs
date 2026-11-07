using System;
using System.Globalization;
using UnityEngine;

namespace Game.Framework.Localization.Unity
{
    public static class LocalizationSetup
    {
        /// <summary>Explicit project-entry setup. Validate before touching the application singleton.</summary>
        public static LocalizationManager Initialize(LocalizationSettings settings, string preferredLanguage = null)
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Initialize localization on the initialized Unity main thread.");
            }
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            var catalog = settings.CreateCatalog();
            string requested = preferredLanguage ?? (settings.useSystemLanguage ? DeviceLanguage() : settings.defaultLanguage);
            string language = settings.ResolveLanguage(catalog, requested);
            bool existed = LocalizationManager.TryGetInstance(out var manager);
            if (existed && manager.IsInitialized)
            {
                throw new InvalidOperationException("Localization is already initialized. Reuse the current service or release its owner before changing settings.");
            }
            manager = existed ? manager : LocalizationManager.Instance;
            try
            {
                manager.Initialize(catalog, language, settings.fallbackLanguage);
                return manager;
            }
            catch
            {
                if (!existed)
                {
                    manager.Dispose();
                }
                throw;
            }
        }

        private static string DeviceLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                    return "zh-Hans";
                case SystemLanguage.ChineseTraditional:
                    return "zh-Hant";
                case SystemLanguage.English:
                    return "en";
                case SystemLanguage.French:
                    return "fr";
                case SystemLanguage.German:
                    return "de";
                case SystemLanguage.Spanish:
                    return "es";
                case SystemLanguage.Japanese:
                    return "ja";
                case SystemLanguage.Korean:
                    return "ko";
                case SystemLanguage.Italian:
                    return "it";
                case SystemLanguage.Portuguese:
                    return "pt";
                case SystemLanguage.Russian:
                    return "ru";
                case SystemLanguage.Arabic:
                    return "ar";
                case SystemLanguage.Dutch:
                    return "nl";
                case SystemLanguage.Turkish:
                    return "tr";
                case SystemLanguage.Polish:
                    return "pl";
                case SystemLanguage.Ukrainian:
                    return "uk";
                case SystemLanguage.Thai:
                    return "th";
                case SystemLanguage.Vietnamese:
                    return "vi";
                case SystemLanguage.Indonesian:
                    return "id";
                case SystemLanguage.Hindi:
                    return "hi";
                default:
                    return CultureInfo.CurrentUICulture.Name;
            }
        }
    }
}
