using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class InputRebindAuditTests
{
    [UnityTest]
    public IEnumerator CancelingAnActiveActionCannotStartAnOverlappingRebind()
    {
        bool ownThread = !UnityThread.IsInitialized;
        if (ownThread)
        {
            UnityThread.Initialize();
        }
        var previousSettings = InputSystem.settings;
        var testSettings = Object.Instantiate(previousSettings);
        testSettings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
        testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        bool previousRunInBackground = Application.runInBackground;
        Keyboard keyboard = null;
        InputActionAsset source = null;
        var input = new InputManager();
        using var cancel = new CancellationTokenSource();
        try
        {
            // Batch-mode Editor has no focused Game View. Use a private settings copy
            // so this synthetic keyboard reaches the actual player action state machine.
            Application.runInBackground = true;
            InputSystem.settings = testSettings;
            keyboard = InputSystem.AddDevice<Keyboard>();
            source = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = source.AddActionMap("Gameplay");
            map.AddAction("Move", InputActionType.Button, "<Keyboard>/space");
            input.Initialize(source, "Gameplay");
            input.SetDevices(keyboard);
            Task<string> nested = null;
            bool rejectedOverride = false;
            using var subscription = input.Subscribe(
                "Move",
                null,
                _ =>
                {
                    if (nested != null)
                    {
                        return;
                    }
                    nested = input.RebindAsync("Move", 0);
                    try
                    {
                        input.LoadBindingOverrides("{}");
                    }
                    catch (InvalidOperationException)
                    {
                        rejectedOverride = true;
                    }
                });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            InputSystem.Update();
            Assert.That(keyboard.spaceKey.isPressed, Is.True, "The synthetic state event must reach the real keyboard device.");
            Assert.That(input.FindAction("Move").phase, Is.EqualTo(InputActionPhase.Performed));
            Task<string> outer = input.RebindAsync("Move", 0, cancel.Token);
            Assert.That(nested, Is.Not.Null, "Disabling the performed action must invoke the project canceled callback.");
            Assert.That(nested.IsFaulted, Is.True, "The outer rebind must reserve its slot before disabling the action.");
            Assert.That(nested.Exception.GetBaseException(), Is.TypeOf<InvalidOperationException>());
            Assert.That(nested.Exception.GetBaseException().Message, Does.Contain("already active"));
            Assert.That(rejectedOverride, Is.True);
            cancel.Cancel();
            float deadline = Time.realtimeSinceStartup + 10;
            while (!outer.IsCompleted)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            Assert.Catch<OperationCanceledException>(() => outer.GetAwaiter().GetResult());
            Assert.That(input.FindAction("Move").enabled, Is.True);
        }
        finally
        {
            input.Dispose();
            Object.Destroy(source);
            if (keyboard != null)
            {
                InputSystem.RemoveDevice(keyboard);
            }
            InputSystem.settings = previousSettings;
            Application.runInBackground = previousRunInBackground;
            Object.Destroy(testSettings);
            if (ownThread)
            {
                UnityThread.Reset();
            }
        }
        yield return null;
    }
}
