using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Audio;
using Game.Framework.Input;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;
using Object = UnityEngine.Object;

internal static class Program
{
    private sealed class CellProbe : MonoBehaviour
    {
    }

    private static int passed;

    private static void Main()
    {
        for (int column = 0; column < 4; column++)
        {
            for (int clear = 0; clear < 2; clear++)
            {
                for (int deferred = 0; deferred < 2; deferred++)
                {
                    int selected = column;
                    bool clearing = clear != 0;
                    bool delayed = deferred != 0;
                    Run("grid unbind column " + selected + ", clear=" + clearing + ", deferred=" + delayed,
                        () => ReentrantGrid(selected, clearing, delayed));
                }
            }
        }
        Run("action cancellation cannot overlap rebinding or replace overrides", () => ReentrantRebinding(0));
        Run("action cancellation can dispose its owner before native rebinding starts", () => ReentrantRebinding(1));
        Run("action cancellation can cancel before native rebinding starts", () => ReentrantRebinding(2));
        Run("audio fade curve validation retains the full supported range", AudioCurveValidation);
        Run("audio fade setup, 32-voice tick and source reuse allocate no warmed memory", AudioAllocations);
        Console.WriteLine("PASS " + passed + " UI/audio/input audit scenarios; actual package sources with finite Unity/Input System models, not native execution.");
    }

    private static void Run(string name, Action test)
    {
        Object.ResetTestState();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new Pump());
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        finally
        {
            Object.DeferGameObjectDestructionForTest = false;
            Object.ResetTestState();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static void ReentrantGrid(int column, bool clearing, bool deferred)
    {
        var host = Create();
        var grid = host.Root.AddComponent<UIVirtualGrid>();
        grid.Configure(host.Scroll, host.Prefab, new Vector2(50, 20), 4);
        var bound = new HashSet<GameObject>();
        var indices = new Dictionary<GameObject, int>();
        int bindings = 0;
        int releases = 0;
        bool reentered = false;
        grid.SetItemCount(
            100,
            (cell, index) =>
            {
                Require(bound.Add(cell), "A cell was rebound without releasing its old binding.");
                indices[cell] = index;
                bindings++;
            },
            cell =>
            {
                Require(bound.Remove(cell), "A cell binding was released twice.");
                releases++;
                if (!reentered && indices[cell] % 4 == column)
                {
                    reentered = true;
                    grid.RefreshItems();
                }
            });
        Object.DeferGameObjectDestructionForTest = deferred;
        if (clearing)
        {
            grid.Clear();
        }
        else
        {
            grid.RefreshItems();
        }
        var active = host.Content.GetComponentsInChildren<CellProbe>().Select(probe => probe.gameObject).ToArray();
        Require(reentered && grid.ItemCount == 100, "The replacement refresh must own the grid state.");
        Require(active.Length == bound.Count && active.All(bound.Contains), "Outer unbind released a replacement row binding.");
        grid.Clear();
        Require(bound.Count == 0 && bindings == releases, "Every original/replacement binding must be released exactly once.");
        Object.FlushDestroyQueueForTest();
        Require(grid.InstantiatedItemCount == 0, "Retirement cannot retain stale row ownership.");
    }

    private static void ReentrantRebinding(int scenario)
    {
        var source = ScriptableObject.CreateInstance<InputActionAsset>();
        var map = new InputActionMap
        {
            name = "Gameplay"
        };
        map.ActionsForTest.Add(new InputAction
        {
            name = "Move",
            actionMap = map,
            bindings = new ReadOnlyArray<InputBinding>(new[] { new InputBinding() })
        });
        source.actionMaps = new ReadOnlyArray<InputActionMap>(new[] { map });
        using var input = new InputManager();
        input.Initialize(source, "Gameplay");
        using var cancel = new CancellationTokenSource();
        Task<string> nested = null;
        bool invoked = false;
        bool overrideRejected = false;
        using var subscription = input.Subscribe(
            "Move",
            null,
            _ =>
            {
                if (invoked)
                {
                    return;
                }
                invoked = true;
                if (scenario == 1)
                {
                    input.Dispose();
                }
                else if (scenario == 2)
                {
                    cancel.Cancel();
                }
                else
                {
                    nested = input.RebindAsync("Move", 0);
                    try
                    {
                        input.LoadBindingOverrides("{}");
                    }
                    catch (InvalidOperationException)
                    {
                        overrideRejected = true;
                    }
                }
            });
        var previousOperation = InputActionRebindingExtensions.LastForTest;
        var outer = input.RebindAsync("Move", 0, cancel.Token);
        Require(invoked, "The model action must dispatch its canceled notification.");
        if (scenario == 0)
        {
            Require(nested != null && nested.IsFaulted && nested.Exception.GetBaseException() is InvalidOperationException,
                "Disabling for a rebind allowed an overlapping rebind.");
            Require(overrideRejected, "The reserved operation must also protect its bindings.");
            InputActionRebindingExtensions.LastForTest.CompleteForTest();
            ((Pump)SynchronizationContext.Current).Wait(outer);
            outer.GetAwaiter().GetResult();
            Require(input.FindAction("Move").enabled, "A completed rebind must restore the requested map state.");
        }
        else
        {
            Require(ReferenceEquals(previousOperation, InputActionRebindingExtensions.LastForTest), "Cancellation/disposal during Disable must prevent creating a native operation.");
            if (scenario == 1)
            {
                Expect<ObjectDisposedException>(() => outer.GetAwaiter().GetResult());
            }
            else
            {
                Expect<OperationCanceledException>(() => outer.GetAwaiter().GetResult());
                Require(input.FindAction("Move").enabled, "An early cancellation must restore map state.");
            }
        }
    }

    private static void AudioCurveValidation()
    {
        var audio = AudioManager.Instance;
        var voice = audio.Play(new AudioClip(), loop: true);
        foreach (var curve in new[] { AudioFadeCurve.Linear, AudioFadeCurve.SmoothStep, AudioFadeCurve.EqualPower })
        {
            Require(voice.FadeTo(.5f, 1, curve), "A declared curve must be supported.");
        }
        foreach (int invalid in new[] { -1, 3, int.MinValue, int.MaxValue })
        {
            Expect<ArgumentOutOfRangeException>(() => voice.FadeTo(.5f, 1, (AudioFadeCurve)invalid));
        }
    }

    private static void AudioAllocations()
    {
        var audio = AudioManager.Instance;
        var clip = new AudioClip();
        var voice = audio.Play(clip, loop: true);
        NoAllocation("fade setup", () => voice.FadeTo(.5f, 100000, AudioFadeCurve.EqualPower));
        for (int i = 1; i < 32; i++)
        {
            audio.Play(clip, loop: true).FadeTo(.5f, 100000);
        }
        NoAllocation("32 fading voice ticks", () => audio.Tick(.001f));
        Require(audio.PlayingCount == 32, "Measured ticks must actually process the full voice set.");
        audio.StopAll();
        NoAllocation("source reuse", () => audio.Play(clip).Stop());
    }

    private static void NoAllocation(string name, Action operation)
    {
        for (int i = 0; i < 1024; i++)
        {
            operation();
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8192; i++)
        {
            operation();
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(bytes == 0, name + " allocated " + bytes + " bytes for 8192 warmed calls.");
    }

    private static void Expect<T>(Action operation) where T : Exception
    {
        try
        {
            operation();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class Pump : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> pending = new ConcurrentQueue<(SendOrPostCallback, object)>();

        public override void Post(SendOrPostCallback callback, object state)
        {
            pending.Enqueue((callback, state));
        }

        internal void Wait(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("The input operation did not settle.");
                }
                while (pending.TryDequeue(out var item))
                {
                    item.Callback(item.State);
                }
                Thread.Yield();
            }
        }
    }

    private static (GameObject Root, ScrollRect Scroll, GameObject Content, GameObject Prefab) Create()
    {
        var root = new GameObject("Audit UI", typeof(RectTransform));
        var viewport = (RectTransform)new GameObject("Viewport", typeof(RectTransform)).transform;
        viewport.SetParent(root.transform, false);
        viewport.anchorMin = viewport.anchorMax = Vector2.zero;
        viewport.sizeDelta = new Vector2(200, 200);
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport, false);
        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = (RectTransform)content.transform;
        var prefab = new GameObject("Cell", typeof(RectTransform));
        prefab.AddComponent<CellProbe>();
        prefab.SetActive(false);
        return (root, scroll, content, prefab);
    }
}
