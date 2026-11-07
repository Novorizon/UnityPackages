using System;
using System.Globalization;
using UnityEngine;
using TMPro;

namespace Game.Framework.Localization.UGUI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class LocalizedText : MonoBehaviour
    {
        [Serializable]
        public sealed class LanguageFont
        {
            public string language;
            public TMP_FontAsset font;
        }

        [SerializeField] private string key;
        [SerializeField] private LanguageFont[] fonts = Array.Empty<LanguageFont>();
        private LocalizationManager manager;
        private TMP_Text target;
        private TMP_FontAsset originalFont;
        private bool capturedFont, subscribed;
        private object[] arguments;

        public void Bind(LocalizationManager service, string translationKey, params object[] formatArguments)
        {
            Unsubscribe();
            manager = service ?? throw new ArgumentNullException(nameof(service));
            key = translationKey;
            arguments = formatArguments == null || formatArguments.Length == 0 ? null : (object[])formatArguments.Clone();
            Subscribe();
            Refresh();
        }

        public void Refresh()
        {
            if (manager == null || !manager.IsInitialized || string.IsNullOrEmpty(key))
            {
                return;
            }

            if (target == null)
            {
                target = GetComponent<TMP_Text>();
                if (target == null)
                {
                    throw new InvalidOperationException("LocalizedText requires a TMP text component. Replace legacy Text components and assign TMP font assets on existing prefabs.");
                }
            }

            if (!capturedFont)
            {
                originalFont = target.font;
                capturedFont = true;
            }

            TMP_FontAsset selected = originalFont;
            bool matched = false;
            var culture = CultureInfo.GetCultureInfo(manager.Language);
            while (culture.Name.Length != 0)
            {
                foreach (var entry in fonts)
                {
                    if (entry != null && entry.font != null && string.Equals(entry.language, culture.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = entry.font;
                        matched = true;
                        break;
                    }
                }

                if (matched)
                {
                    break;
                }

                culture = culture.Parent;
            }

            target.font = selected;
            target.text = arguments == null ? manager.Get(key) : manager.Format(key, arguments);
        }

        private void OnEnable()
        {
            if (manager == null || !manager.IsInitialized)
            {
                LocalizationManager.TryGetInstance(out manager);
            }

            Subscribe();
            Refresh();
        }

        private void Subscribe()
        {
            if (!subscribed && isActiveAndEnabled && manager != null && manager.IsInitialized)
            {
                manager.LanguageChanged += OnLanguageChanged;
                subscribed = true;
            }
        }

        private void OnLanguageChanged(string language) => Refresh();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy()
        {
            Unsubscribe();
            manager = null;
            arguments = null;
        }

        private void Unsubscribe()
        {
            if (subscribed && manager != null)
            {
                manager.LanguageChanged -= OnLanguageChanged;
            }

            subscribed = false;
        }
    }
}
