using UI;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;
// Axis-aligned screen geometry contracts, not Unity camera/raycast or CanvasScaler certification.
public sealed class UISafeAreaTests : IDisposable
{
    public UISafeAreaTests()
    {
        Object.ResetTestState();
    }

    public void Dispose()
    {
        Object.ResetTestState();
    }

    static (Canvas Canvas, RectTransform Rect) CreateCanvas(int width = 1000, int height = 2000, float scale = 1)
    {
        Screen.SetForTest(width, height, new Rect(0, 0, width, height));
        var go = new GameObject("Safe Area Canvas", typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.scaleFactor = scale;
        var rect = (RectTransform)go.transform;
        SetGeometry(rect, width / scale, height / scale, width / 2f, height / 2f, scale);
        return (canvas, rect);
    }

    static void SetGeometry(RectTransform rect, float width, float height, float centerX, float centerY, float scale = 1)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = new Vector3(scale, scale, 1);
        rect.position = new Vector3(centerX, centerY, 0);
    }

    static (UISafeArea Safe, RectTransform Rect) CreateContent(RectTransform parent)
    {
        var go = new GameObject("Safe Content", typeof(RectTransform));
        go.SetActive(false);
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return (go.AddComponent<UISafeArea>(), rect);
    }

    static void AssertVector(Vector2 expected, Vector2 actual)
    {
        Assert.InRange(actual.x, expected.x - .0001f, expected.x + .0001f);
        Assert.InRange(actual.y, expected.y - .0001f, expected.y + .0001f);
    }

    static void AssertAnchors(RectTransform rect, float minX, float minY, float maxX, float maxY)
    {
        AssertVector(new Vector2(minX, minY), rect.anchorMin);
        AssertVector(new Vector2(maxX, maxY), rect.anchorMax);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SafeAreaInsetsContentInScreenPixelsAcrossCanvasScalesAndLeavesBackgroundUnchanged(float scale)
    {
        var(_, canvas) = CreateCanvas(scale: scale);
        Screen.SetForTest(1000, 2000, new Rect(20, 100, 960, 1800));
        var background = new GameObject("Full Screen Background", typeof(RectTransform));
        var backgroundRect = (RectTransform)background.transform;
        backgroundRect.SetParent(canvas, false);
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = backgroundRect.offsetMax = Vector2.zero;
        var(safe, rect) = CreateContent(canvas);
        rect.gameObject.SetActive(true);
        Assert.True(safe.IsApplied);
        AssertAnchors(rect, .02f, .05f, .98f, .95f);
        AssertVector(Vector2.zero, rect.offsetMin);
        AssertVector(Vector2.zero, rect.offsetMax);
        AssertAnchors(backgroundRect, 0, 0, 1, 1);
    }

    [Fact]
    public void AParentAlreadyInsideTheSafeAreaDoesNotReceiveTheScreenInsetsTwice()
    {
        var(_, canvas) = CreateCanvas();
        Screen.SetForTest(1000, 2000, new Rect(100, 200, 800, 1600));
        var panel = new GameObject("Centered Panel", typeof(RectTransform));
        var panelRect = (RectTransform)panel.transform;
        panelRect.SetParent(canvas, false);
        SetGeometry(panelRect, 500, 1000, 500, 1000);
        var(safe, rect) = CreateContent(panelRect);
        rect.gameObject.SetActive(true);
        Assert.True(safe.IsApplied);
        AssertAnchors(rect, 0, 0, 1, 1);
    }

    [Fact]
    public void CameraViewportIsClippedBeforeConvertingIntoParentAnchors()
    {
        var(canvas, canvasRect) = CreateCanvas();
        var camera = new GameObject("Pixel Plane UI Camera").AddComponent<Camera>();
        camera.pixelRect = new Rect(200, 400, 600, 1200);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        SetGeometry(canvasRect, 600, 1200, 500, 1000);
        Screen.SetForTest(1000, 2000, new Rect(0, 600, 1000, 1000));
        var(safe, rect) = CreateContent(canvasRect);
        rect.gameObject.SetActive(true);
        Assert.True(safe.IsApplied);
        AssertAnchors(rect, 0, 1f / 6, 1, 1);
        camera.pixelRect = new Rect(200, 600, 600, 800);
        SetGeometry(canvasRect, 600, 800, 500, 1000);
        Object.AdvanceFrameForTest();
        AssertAnchors(rect, 0, 0, 1, 1);
    }

    [Fact]
    public void RotationAndScreenSizeChangesRefreshTheExistingContainer()
    {
        var(_, canvas) = CreateCanvas();
        Screen.SetForTest(1000, 2000, new Rect(20, 100, 960, 1800));
        var(safe, rect) = CreateContent(canvas);
        rect.gameObject.SetActive(true);
        AssertAnchors(rect, .02f, .05f, .98f, .95f);
        Screen.SetForTest(2000, 1000, new Rect(100, 20, 1800, 960));
        SetGeometry(canvas, 2000, 1000, 1000, 500);
        Object.AdvanceFrameForTest();
        Assert.True(safe.IsApplied);
        AssertAnchors(rect, .05f, .02f, .95f, .98f);
    }

    [Fact]
    public void ParentTranslationWithoutScreenChangesInvalidatesTheProjection()
    {
        var(_, canvas) = CreateCanvas();
        Screen.SetForTest(1000, 2000, new Rect(100, 200, 800, 1600));
        var parent = new GameObject("Moving Content Parent", typeof(RectTransform));
        var parentRect = (RectTransform)parent.transform;
        parentRect.SetParent(canvas, false);
        SetGeometry(parentRect, 1000, 2000, 500, 1000);
        var(_, rect) = CreateContent(parentRect);
        rect.gameObject.SetActive(true);
        AssertAnchors(rect, .1f, .1f, .9f, .9f);
        parentRect.position = new Vector3(600, 1000, 0);
        Object.AdvanceFrameForTest();
        AssertAnchors(rect, 0, .1f, .8f, .9f);
    }

    [Fact]
    public void DisablingRestoresAuthoredAnchorsAndOffsetsAndReenableAppliesAgain()
    {
        var(_, canvas) = CreateCanvas();
        Screen.SetForTest(1000, 2000, new Rect(100, 200, 800, 1600));
        var(safe, rect) = CreateContent(canvas);
        rect.anchorMin = new Vector2(.1f, .2f);
        rect.anchorMax = new Vector2(.9f, .8f);
        rect.offsetMin = new Vector2(10, 20);
        rect.offsetMax = new Vector2(-10, -20);
        rect.gameObject.SetActive(true);
        AssertAnchors(rect, .1f, .1f, .9f, .9f);
        safe.enabled = false;
        Assert.False(safe.IsApplied);
        AssertAnchors(rect, .1f, .2f, .9f, .8f);
        AssertVector(new Vector2(10, 20), rect.offsetMin);
        AssertVector(new Vector2(-10, -20), rect.offsetMax);
        safe.enabled = true;
        Assert.True(safe.IsApplied);
        AssertAnchors(rect, .1f, .1f, .9f, .9f);
    }

    [Fact]
    public void DisablingAnAxisRestoresOnlyThatAxisAndDoesNotOverwriteUnfittedChanges()
    {
        var(_, canvas) = CreateCanvas();
        Screen.SetForTest(1000, 2000, new Rect(100, 200, 800, 1600));
        var(safe, rect) = CreateContent(canvas);
        safe.FitHorizontal = false;
        rect.anchorMin = new Vector2(.1f, .2f);
        rect.anchorMax = new Vector2(.9f, .8f);
        rect.offsetMin = new Vector2(10, 20);
        rect.offsetMax = new Vector2(-10, -20);
        rect.gameObject.SetActive(true);
        AssertAnchors(rect, .1f, .1f, .9f, .9f);
        Assert.Equal(10, rect.offsetMin.x);
        Assert.Equal(-10, rect.offsetMax.x);
        rect.anchorMin = new Vector2(.15f, rect.anchorMin.y);
        rect.anchorMax = new Vector2(.85f, rect.anchorMax.y);
        safe.FitVertical = false;
        Assert.False(safe.IsApplied);
        AssertAnchors(rect, .15f, .2f, .85f, .8f);
        AssertVector(new Vector2(10, 20), rect.offsetMin);
        AssertVector(new Vector2(-10, -20), rect.offsetMax);
        safe.enabled = false;
        AssertAnchors(rect, .15f, .2f, .85f, .8f);
    }

    [Fact]
    public void StableFramesDoNotRepeatedlyAssignLayoutProperties()
    {
        var(_, canvas) = CreateCanvas();
        Screen.SetForTest(1000, 2000, new Rect(100, 200, 800, 1600));
        var(_, rect) = CreateContent(canvas);
        rect.gameObject.SetActive(true);
        int writes = rect.LayoutWriteCountForTest;
        for (int i = 0; i < 120; i++)
        {
            Object.AdvanceFrameForTest();
        }

        Assert.Equal(writes, rect.LayoutWriteCountForTest);
        Screen.SetForTest(1000, 2000, new Rect(50, 100, 900, 1800));
        Object.AdvanceFrameForTest();
        Assert.True(rect.LayoutWriteCountForTest > writes);
        AssertAnchors(rect, .05f, .05f, .95f, .95f);
    }

    [Fact]
    public void UnsupportedWorldSpaceOrInvalidScreenRestoresOwnedLayoutAndCanRecover()
    {
        var(canvas, canvasRect) = CreateCanvas();
        Screen.SetForTest(1000, 2000, new Rect(100, 200, 800, 1600));
        var(safe, rect) = CreateContent(canvasRect);
        rect.gameObject.SetActive(true);
        Assert.True(safe.IsApplied);
        canvas.renderMode = RenderMode.WorldSpace;
        Object.AdvanceFrameForTest();
        Assert.False(safe.IsApplied);
        AssertAnchors(rect, 0, 0, 1, 1);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Object.AdvanceFrameForTest();
        Assert.True(safe.IsApplied);
        Screen.SetForTest(0, 0, default);
        Object.AdvanceFrameForTest();
        Assert.False(safe.IsApplied);
        AssertAnchors(rect, 0, 0, 1, 1);
        Screen.SetForTest(1000, 2000, new Rect(0, 0, 1000, 2000));
        Object.AdvanceFrameForTest();
        Assert.True(safe.IsApplied);
        AssertAnchors(rect, 0, 0, 1, 1);
    }
}