using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Localization
{
    /// <summary>Main-thread service. Prepare fonts/assets before committing a language change.</summary>
    public sealed class LocalizationManager : ConfigurableSingleton<LocalizationManager>, IDisposable
    {
        private LocalizationCatalog catalog;
        private CultureInfo current, fallback;
        private CancellationTokenSource pending;
        private long revision;
        private long publication;
        private bool disposed;

        public bool IsInitialized => catalog != null && !disposed;
        public string Language => current?.Name;
        public string FallbackLanguage => fallback?.Name;
        public int KeyCount => catalog?.KeyCount ?? 0;
        public event Action<string> LanguageChanged;

        public override void Initialize()
        {
            RequireInitialized();
        }

        public void Initialize(LocalizationCatalog translations, string language, string fallbackLanguage = "en")
        {
            MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(LocalizationManager));
            }

            if (IsInitialized)
            {
                throw new InvalidOperationException("Release the existing localization service before replacing its catalog.");
            }

            var selected = Culture(language);
            var defaultCulture = Culture(fallbackLanguage);
            if (translations == null || !translations.ContainsLanguage(defaultCulture.Name))
            {
                throw new ArgumentException("The fallback language must have a catalog column.", nameof(translations));
            }

            catalog = translations;
            current = selected;
            fallback = defaultCulture;
        }

        public string Get(string key)
        {
            RequireInitialized();
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Translation key is required.", nameof(key));
            }

            return catalog.TryGet(key, current, out var text) || catalog.TryGet(key, fallback, out text) ? text : key;
        }

        public string Format(string key, params object[] arguments)
        {
            string text = Get(key);
            return string.Format(current, text, arguments ?? throw new ArgumentNullException(nameof(arguments)));
        }

        public void SetLanguage(string language)
        {
            RequireInitialized();
            var selected = Culture(language);
            long operation = ++revision;
            CancelPending();
            if (revision == operation)
            {
                Commit(selected);
            }
        }

        /// <summary>Latest request wins. A failed/cancelled preparation preserves the displayed language.</summary>
        public async Task SetLanguageAsync(string language, Func<string, CancellationToken, Task> prepare, CancellationToken token = default)
        {
            RequireInitialized();
            var selected = Culture(language);
            if (prepare == null)
            {
                throw new ArgumentNullException(nameof(prepare));
            }

            token.ThrowIfCancellationRequested();
            long operation = ++revision;
            CancelPending();
            if (revision != operation || disposed)
            {
                throw new OperationCanceledException("A cancellation callback started a newer language change.");
            }

            var source = CancellationTokenSource.CreateLinkedTokenSource(token);
            pending = source;
            try
            {
                await TaskUtil.WaitAsync(prepare(selected.Name, source.Token), source.Token);
                await UnityThread.SwitchToMainThread(source.Token);
                source.Token.ThrowIfCancellationRequested();
                if (disposed || revision != operation)
                {
                    throw new OperationCanceledException(source.Token);
                }

                Commit(selected);
            }
            finally
            {
                // This continuation may run off-thread when cancellation rejects a queued switch.
                Interlocked.CompareExchange(ref pending, null, source);
                source.Dispose();
            }
        }

        private void Commit(CultureInfo selected)
        {
            if (current.Name == selected.Name)
            {
                return;
            }

            current = selected;
            long notification = ++publication;
            var handlers = LanguageChanged;
            if (handlers == null)
            {
                return;
            }

            foreach (Action<string> handler in handlers.GetInvocationList())
            {
                if (disposed || publication != notification)
                {
                    break;
                }

                try
                {
                    handler(selected.Name);
                }
                catch (Exception error)
                {
                    Logger.Exception(error);
                }
            }
        }

        private void CancelPending()
        {
            var source = Interlocked.Exchange(ref pending, null);
            if (source == null)
            {
                return;
            }

            try
            {
                source.Cancel();
            }
            catch (Exception error)
            {
                Logger.Exception(error);
            }
        }

        private static CultureInfo Culture(string language)
        {
            var culture = CultureInfo.GetCultureInfo(language);
            if (culture.Name.Length == 0)
            {
                throw new ArgumentException("Use an explicit language.", nameof(language));
            }

            return culture;
        }

        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use localization on the initialized Unity main thread.");
            }
        }

        private void RequireInitialized()
        {
            MainThread();
            if (!IsInitialized)
            {
                throw new InvalidOperationException("Initialize localization with a catalog before use.");
            }
        }

        public void Dispose()
        {
            if (UnityThread.IsInitialized)
            {
                MainThread();
            }
            disposed = true;
            revision++;
            CancelPending();
            LanguageChanged = null;
            catalog = null;
        }
    }
}
