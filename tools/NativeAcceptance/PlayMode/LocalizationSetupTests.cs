using System;
using System.Collections;
using Game.Framework;
using Game.Framework.Localization;
using Game.Framework.Localization.UGUI;
using Game.Framework.Localization.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class LocalizationSetupTests
{
    private bool ownsThreadHost;
    private LocalizationTableAsset table;
    private LocalizationSettings settings;
    private LocalizationManager service;

    [SetUp]
    public void SetUp()
    {
        Assert.That(LocalizationManager.TryGetInstance(out _), Is.False, "Previous test must release its localization owner.");
        ownsThreadHost = !UnityThread.IsInitialized;
        if (ownsThreadHost)
        {
            UnityThread.Initialize();
        }
        table = ScriptableObject.CreateInstance<LocalizationTableAsset>();
        table.SetData(LocalizationDelimitedText.Parse("key,en,fr\nui.start,Start Game,Jouer\n"));
        settings = ScriptableObject.CreateInstance<LocalizationSettings>();
        settings.table = table;
        settings.defaultLanguage = "en";
        settings.fallbackLanguage = "en";
        settings.useSystemLanguage = false;
    }

    [TearDown]
    public void TearDown()
    {
        service?.Dispose();
        service = null;
        Object.DestroyImmediate(settings);
        Object.DestroyImmediate(table);
        if (ownsThreadHost)
        {
            UnityThread.Reset();
        }
    }

    [Test]
    public void InvalidSettingsDoNotCreateAService()
    {
        settings.fallbackLanguage = "ja";
        Assert.Throws<InvalidOperationException>(() => LocalizationSetup.Initialize(settings));
        Assert.That(LocalizationManager.TryGetInstance(out _), Is.False);
    }

    [Test]
    public void ConfiguredSetupChoosesLanguageAndKeepsThePublishedSnapshot()
    {
        service = LocalizationSetup.Initialize(settings, "fr-CA");
        Assert.That(service.Language, Is.EqualTo("fr"));
        Assert.That(service.Get("ui.start"), Is.EqualTo("Jouer"));
        table.SetData(LocalizationDelimitedText.Parse("key,en,fr\nui.start,Changed,Modifie\n"));
        Assert.That(service.Get("ui.start"), Is.EqualTo("Jouer"));
        Assert.Throws<InvalidOperationException>(() => LocalizationSetup.Initialize(settings));
        Assert.That(service.IsInitialized, Is.True);
    }

    [Test]
    public void SettingsMapChineseDeviceRegionsToAuthoredScripts()
    {
        table.SetData(LocalizationDelimitedText.Parse("key,en,zh-Hans,zh-Hant\nui.start,Start,A,B\n"));
        Assert.That(settings.ResolveLanguage("zh-HK"), Is.EqualTo("zh-Hant"));
        Assert.That(settings.ResolveLanguage("zh-CN"), Is.EqualTo("zh-Hans"));
        Assert.That(settings.ResolveLanguage("de-DE"), Is.EqualTo("en"));
    }

    [Test]
    public void OldBootstrapCannotReleaseAReplacementService()
    {
        var entry = new GameObject("Localization owner");
        entry.SetActive(false);
        var bootstrap = entry.AddComponent<LocalizationBootstrap>();
        bootstrap.InitializeOnAwake = false;
        bootstrap.Settings = settings;
        try
        {
            var first = bootstrap.Initialize();
            Assert.That(bootstrap.Initialize(), Is.SameAs(first));
            first.Dispose();
            service = LocalizationSetup.Initialize(settings, "fr");
            bootstrap.Shutdown();
            Assert.That(service.IsInitialized, Is.True);
            Assert.That(LocalizationManager.TryGetInstance(out var current), Is.True);
            Assert.That(current, Is.SameAs(service));
            Assert.That(service.Get("ui.start"), Is.EqualTo("Jouer"));
        }
        finally
        {
            bootstrap.Shutdown();
            Object.DestroyImmediate(entry);
        }
    }

    [UnityTest]
    public IEnumerator InspectorKeyUsesConfiguredBootstrapWithoutDataManagerOrBind()
    {
        Assert.That(TMP_Settings.defaultFontAsset, Is.Not.Null, "Import TMP Essential Resources before native acceptance.");
        var entry = new GameObject("Localization project entry");
        entry.SetActive(false);
        var bootstrap = entry.AddComponent<LocalizationBootstrap>();
        bootstrap.Settings = settings;
        var label = new GameObject("Inspector configured label", typeof(RectTransform));
        label.SetActive(false);
        var text = label.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        var binding = label.AddComponent<LocalizedText>();
        JsonUtility.FromJsonOverwrite("{\"key\":\"ui.start\"}", binding);
        try
        {
            // An enabled label cannot initialize or manufacture a global service.
            label.SetActive(true);
            yield return null;
            Assert.That(LocalizationManager.TryGetInstance(out _), Is.False);
            label.SetActive(false);
            entry.SetActive(true);
            var owned = bootstrap.Manager;
            Assert.That(owned, Is.Not.Null, "The configured entry must initialize in Awake before UI opens.");
            label.SetActive(true);
            yield return null;
            Assert.That(text.text, Is.EqualTo("Start Game"));
            owned.SetLanguage("fr");
            Assert.That(text.text, Is.EqualTo("Jouer"));
            label.SetActive(false);
            owned.SetLanguage("en");
            label.SetActive(true);
            yield return null;
            Assert.That(text.text, Is.EqualTo("Start Game"));
            label.SetActive(false);
            bootstrap.Shutdown();
            Assert.That(LocalizationManager.TryGetInstance(out _), Is.False);
        }
        finally
        {
            label.SetActive(false);
            bootstrap.Shutdown();
            Object.DestroyImmediate(label);
            Object.DestroyImmediate(entry);
        }
    }
}
