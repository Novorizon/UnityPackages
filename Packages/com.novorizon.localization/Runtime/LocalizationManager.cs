using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Localization
{
    /// <summary>Main-thread service. Prepare fonts/assets before committing a language change.</summary>
    /// <remarks>本地化对应的管理器。管理对应模块的状态和操作入口。</remarks>
    public sealed class LocalizationManager : Singleton<LocalizationManager>, IDisposable
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

        /// <summary>初始化本地化管理器。</summary>
        public override void Initialize()
        {
            RequireInitialized();
        }

        /// <summary>初始化本地化管理器。</summary>
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

        /// <summary>先查当前语言，再查回退语言；两者均缺失时返回原始键。</summary>
        public string Get(string key)
        {
            RequireInitialized();
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Translation key is required.", nameof(key));
            }

            return catalog.TryGet(key, current, out var text) || catalog.TryGet(key, fallback, out text) ? text : key;
        }

        /// <summary>格式化传入的 key、arguments。</summary>
        public string Format(string key, params object[] arguments)
        {
            string text = Get(key);
            return string.Format(current, text, arguments ?? throw new ArgumentNullException(nameof(arguments)));
        }

        /// <summary>设置语言。</summary>
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
        /// <remarks>先准备目标语言资源，准备成功后再提交语言切换和通知。</remarks>
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

        /// <summary>提交本地化管理器。</summary>
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

        // 取消上一轮尚未完成的语言准备，避免较慢的旧请求覆盖新语言。
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

        /// <summary>根据传入参数取得区域设置。</summary>
        private static CultureInfo Culture(string language)
        {
            var culture = CultureInfo.GetCultureInfo(language);
            if (culture.Name.Length == 0)
            {
                throw new ArgumentException("Use an explicit language.", nameof(language));
            }

            return culture;
        }

        /// <summary>校验当前调用位于 Unity 主线程。</summary>
        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use localization on the initialized Unity main thread.");
            }
        }

        /// <summary>检查已初始化是否满足前置条件。</summary>
        private void RequireInitialized()
        {
            MainThread();
            if (!IsInitialized)
            {
                throw new InvalidOperationException("Initialize localization with a catalog before use.");
            }
        }

        /// <summary>结束本地化管理器对相关资源的持有，并执行该类型的清理流程。</summary>
        public override void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (UnityThread.IsInitialized)
            {
                MainThread();
            }
            disposed = true;
            revision++;
            try
            {
                CancelPending();
            }
            finally
            {
                LanguageChanged = null;
                catalog = null;
                base.Release();
            }
        }
    }
}
