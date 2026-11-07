using Game.Framework;
using Game.Framework.Localization;
using Game.Framework.Localization.UGUI;
using UnityEngine;
using TMPro;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;

public sealed class LocalizationBindingTests : IDisposable
{
    readonly LocalizationManager manager;
    public LocalizationBindingTests()
    {
        Object.ResetTestState();
        UnityThread.Reset();
        UnityThread.InitializeManual();
        var rows = new[] { new[] { "key", "Hello {0}", "你好 {0}" } };
        var catalog = LocalizationCatalog.FromRows(rows, row => row[0], new TranslationColumn<string[]>("en", row => row[1]), new TranslationColumn<string[]>("zh-Hans", row => row[2]));
        manager = new LocalizationManager();
        manager.Initialize(catalog, "en");
    }
    public void Dispose()
    {
        Object.ResetTestState();
        manager.Dispose();
        UnityThread.Reset();
    }

    [Fact]
    public void OpenBindingCopiesArgumentsAndRefreshesAfterAHiddenWindowIsReopened()
    {
        var root = new GameObject("Localized label");
        var target = root.AddComponent<TextMeshProUGUI>();
        var binding = root.AddComponent<LocalizedText>();
        object[] arguments = { 7 };
        binding.Bind(manager, "key", arguments);
        arguments[0] = 99;
        Assert.Equal("Hello 7", target.text);
        manager.SetLanguage("zh-Hans");
        Assert.Equal("你好 7", target.text);
        root.SetActive(false);
        manager.SetLanguage("en");
        Assert.Equal("你好 7", target.text);
        root.SetActive(true);
        Assert.Equal("Hello 7", target.text);
        Object.Destroy(binding);
        manager.SetLanguage("zh-Hans");
        Assert.Equal("Hello 7", target.text);
    }

    [Fact]
    public void RebindingMovesTheSubscriptionToTheNewService()
    {
        using var next = new LocalizationManager();
        var catalog = LocalizationCatalog.FromRows(new[] { "value" }, row => "key", new TranslationColumn<string>("en", row => row));
        next.Initialize(catalog, "en");
        var root = new GameObject("Label");
        var target = root.AddComponent<TextMeshProUGUI>();
        var binding = root.AddComponent<LocalizedText>();
        binding.Bind(manager, "key", 1);
        binding.Bind(next, "key");
        manager.SetLanguage("zh-Hans");
        Assert.Equal("value", target.text);
    }

    [Fact]
    public void ParentLanguageTmpFontIsRestoredToTheAuthoredFontWhenSwitchingBack()
    {
        var root = new GameObject("TMP font fallback label");
        var target = root.AddComponent<TextMeshProUGUI>();
        var original = ScriptableObject.CreateInstance<TMP_FontAsset>();
        var chinese = ScriptableObject.CreateInstance<TMP_FontAsset>();
        target.font = original;
        var binding = root.AddComponent<LocalizedText>();
        typeof(LocalizedText).GetField("fonts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(binding, new[]
        {
            new LocalizedText.LanguageFont { language = "zh-Hans", font = chinese }
        });
        binding.Bind(manager, "key", 1);
        manager.SetLanguage("zh-Hans-CN");
        Assert.Same(chinese, target.font);
        Assert.Equal("你好 1", target.text);
        manager.SetLanguage("en");
        Assert.Same(original, target.font);
        Assert.Equal("Hello 1", target.text);
    }

    [Fact]
    public void ExactFontOverrideWinsEvenWhenItEqualsTheOriginalFont()
    {
        var root = new GameObject("Font label");
        var target = root.AddComponent<TextMeshProUGUI>();
        var original = ScriptableObject.CreateInstance<TMP_FontAsset>();
        var parent = ScriptableObject.CreateInstance<TMP_FontAsset>();
        target.font = original;
        var binding = root.AddComponent<LocalizedText>();
        typeof(LocalizedText).GetField("fonts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(binding, new[]
        {
            new LocalizedText.LanguageFont { language = "zh-Hans-CN", font = original },
            new LocalizedText.LanguageFont { language = "zh-Hans", font = parent }
        });
        binding.Bind(manager, "key", 1);
        manager.SetLanguage("zh-Hans-CN");
        Assert.Same(original, target.font);
    }
}
