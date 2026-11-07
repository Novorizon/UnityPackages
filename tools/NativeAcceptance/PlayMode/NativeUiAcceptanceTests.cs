using System;
using System.Collections;
using System.Linq;
using Game.Framework;
using Game.Framework.Localization;
using Game.Framework.Localization.UGUI;
using NUnit.Framework;
using UI;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class NativeUiAcceptanceTests
{
    [UnityTest]
    public IEnumerator DynamicRowsSeekAndReleaseWithNativeScrollRectAndDeferredDestroy()
    {
        var canvasRoot = new GameObject("Acceptance Canvas", typeof(RectTransform), typeof(Canvas));
        var root = new GameObject("List", typeof(RectTransform), typeof(ScrollRect));
        var prefab = new GameObject("Cell prefab", typeof(RectTransform));
        prefab.SetActive(false);
        root.transform.SetParent(canvasRoot.transform, false);
        canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(.5f, .5f);
        rootRect.sizeDelta = new Vector2(200, 200);
        var viewport = (RectTransform)new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).transform;
        viewport.SetParent(root.transform, false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.sizeDelta = Vector2.zero;
        var content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
        content.SetParent(viewport, false);
        var scroll = root.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var list = root.AddComponent<UIVirtualDynamicList>();
        try
        {
            Canvas.ForceUpdateCanvases();
            list.Configure(scroll, prefab, 20);
            list.SetItems(
                Enumerable.Range(0, 10000).Select(index => index.ToString()).ToArray(),
                (cell, index) => cell.name = "Row " + index);
            yield return null;
            Assert.That(list.InstantiatedItemCount, Is.InRange(10, 13));
            Assert.That(list.FirstVisibleIndex, Is.EqualTo(0));
            Assert.That(list.LastVisibleIndex, Is.EqualTo(9));
            Assert.That(list.ReportHeight("0", 80, list.DataVersion), Is.True);
            list.ScrollTo(5000);
            yield return null;
            Assert.That(content.anchoredPosition.y, Is.EqualTo(100060).Within(.1f));
            Assert.That(list.FirstVisibleIndex, Is.EqualTo(5000));
            Assert.That(list.InstantiatedItemCount, Is.LessThanOrEqualTo(13));
            var ownedCell = content.GetChild(0).gameObject;
            list.Clear();
            Assert.That(list.InstantiatedItemCount, Is.Zero);
            Assert.That(ownedCell == null || !ownedCell.activeSelf, Is.True);
            yield return null;
            Assert.That(ownedCell == null, Is.True, "Native destruction must complete before releasing the prefab lease.");
            Assert.That(prefab != null, Is.True);
        }
        finally
        {
            Object.Destroy(canvasRoot);
            Object.Destroy(prefab);
        }
    }

    [UnityTest]
    public IEnumerator LocalizedTextRefreshesAcrossLanguageChangesAndCachedViewReactivation()
    {
        Assert.That(TMP_Settings.instance, Is.Not.Null, "Import TMP Essential Resources before native acceptance.");
        var font = TMP_Settings.defaultFontAsset;
        Assert.That(font, Is.Not.Null, "Configure a default TMP font before native acceptance.");
        bool ownThreadHost = !UnityThread.IsInitialized;
        if (ownThreadHost)
        {
            UnityThread.Initialize();
        }
        var service = new LocalizationManager();
        var root = new GameObject("Localized text", typeof(RectTransform));
        root.SetActive(false);
        var text = root.AddComponent<TextMeshProUGUI>();
        text.font = font;
        var binding = root.AddComponent<LocalizedText>();
        try
        {
            var rows = new[] { new[] { "ui.start", "Start Game", "Jouer" } };
            service.Initialize(
                LocalizationCatalog.FromRows(
                    rows,
                    row => row[0],
                    new TranslationColumn<string[]>("en", row => row[1]),
                    new TranslationColumn<string[]>("fr", row => row[2])),
                "en");
            binding.Bind(service, "ui.start");
            root.SetActive(true);
            yield return null;
            Assert.That(text.text, Is.EqualTo("Start Game"));
            Assert.That(text.font, Is.SameAs(font));
            service.SetLanguage("fr");
            Assert.That(text.text, Is.EqualTo("Jouer"));
            root.SetActive(false);
            service.SetLanguage("en");
            root.SetActive(true);
            yield return null;
            Assert.That(text.text, Is.EqualTo("Start Game"));
        }
        finally
        {
            root.SetActive(false);
            Object.Destroy(root);
            service.Dispose();
            if (ownThreadHost)
            {
                UnityThread.Reset();
            }
        }
    }

    [UnityTest]
    public IEnumerator UiDemoBuildsTmpLabelsAndVirtualRowsWithTheActualFont()
    {
        Assert.That(TMP_Settings.defaultFontAsset, Is.Not.Null);
        var root = new GameObject("TMP demo acceptance");
        root.SetActive(false);
        var launcher = root.AddComponent<UI.Demo.UIDemoLauncher>();
        try
        {
            var start = launcher.StartDemoAsync();
            while (!start.IsCompleted)
            {
                yield return null;
            }
            start.GetAwaiter().GetResult();
            Assert.That(launcher.IsStarted, Is.True);
            var inventory = launcher.ShowInventoryAsync();
            while (!inventory.IsCompleted)
            {
                yield return null;
            }
            inventory.GetAwaiter().GetResult();
            Canvas.ForceUpdateCanvases();
            var labels = launcher.Root.GetComponentsInChildren<TMP_Text>(true);
            Assert.That(labels.Length, Is.GreaterThan(5));
            Assert.That(launcher.Root.GetComponentsInChildren<Text>(true), Is.Empty);
            var visible = labels.First(label => label.gameObject.activeInHierarchy && !string.IsNullOrEmpty(label.text));
            visible.ForceMeshUpdate();
            Assert.That(visible.font, Is.SameAs(TMP_Settings.defaultFontAsset));
            Assert.That(visible.textInfo.characterCount, Is.GreaterThan(0));
        }
        finally
        {
            launcher.StopDemo();
            Object.Destroy(root);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator SafeContentProjectsTheActualScreenSafeAreaWithoutShrinkingBackground()
    {
        var root = new GameObject("Safe area canvas", typeof(RectTransform), typeof(Canvas));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var background = (RectTransform)new GameObject("Background", typeof(RectTransform)).transform;
        var content = (RectTransform)new GameObject("SafeContent", typeof(RectTransform)).transform;
        foreach (var rect in new[] { background, content })
        {
            rect.SetParent(root.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        var safeArea = content.gameObject.AddComponent<UISafeArea>();
        try
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(safeArea.Apply(), Is.True);
            var safe = Screen.safeArea;
            Assert.That(content.anchorMin.x, Is.EqualTo(safe.xMin / Screen.width).Within(.001f));
            Assert.That(content.anchorMin.y, Is.EqualTo(safe.yMin / Screen.height).Within(.001f));
            Assert.That(content.anchorMax.x, Is.EqualTo(safe.xMax / Screen.width).Within(.001f));
            Assert.That(content.anchorMax.y, Is.EqualTo(safe.yMax / Screen.height).Within(.001f));
            Assert.That(background.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(background.anchorMax, Is.EqualTo(Vector2.one));
        }
        finally
        {
            Object.Destroy(root);
        }
    }
}
