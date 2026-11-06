using System;
using System.Linq;
using System.Reflection;
using Game.Framework;
using Game.Framework.Timing;
using UnityEngine;
using Object = UnityEngine.Object;

internal static partial class Program
{
    private static void TimerDriverScenarios()
    {
        RunTimerDriver("Unity driver install/update never creates an unused or released timer service", driver =>
        {
            True(!TimerManager.IsCreated);
            TickTimerDriver(driver);
            True(!TimerManager.IsCreated);
            var timer = TimerManager.Instance;
            int fired = 0;
            var pending = timer.Schedule(1, () => fired++);
            TickTimerDriver(driver);
            TimerManager.Release();
            True(!pending.IsActive && !TimerManager.IsCreated);
            for (int i = 0; i < 3; i++)
            {
                Time.timeAsDouble += 2;
                Time.realtimeSinceStartupAsDouble += 2;
                TickTimerDriver(driver);
                True(!TimerManager.IsCreated);
            }

            Equal(0, fired);
        });
        RunTimerDriver("explicit replacement timers retain queued delays and are not rebound each frame", driver =>
        {
            var original = TimerManager.Instance;
            TickTimerDriver(driver);
            TimerManager.Release();
            var replacement = TimerManager.Instance;
            True(!ReferenceEquals(original, replacement));
            int fired = 0;
            replacement.Schedule(1, () => fired++);
            TickTimerDriver(driver);
            Time.timeAsDouble += .4;
            TickTimerDriver(driver);
            Equal(0, fired);
            Time.timeAsDouble += .7;
            TickTimerDriver(driver);
            Equal(1, fired);
            replacement.Schedule(.5, () => fired++);
            Time.timeAsDouble += .2;
            TickTimerDriver(driver);
            Equal(1, fired);
            Time.timeAsDouble += .4;
            TickTimerDriver(driver);
            Equal(2, fired);
        });
        RunTimerDriver("Unity driver preserves existing custom clocks and their active timers", driver =>
        {
            var timer = TimerManager.Instance;
            double manualGame = 7, manualReal = 70;
            timer.UseClocks(() => manualGame, () => manualReal);
            int fired = 0;
            timer.Schedule(.5, () => fired++);
            Time.timeAsDouble = 100000;
            Time.realtimeSinceStartupAsDouble = 200000;
            TickTimerDriver(driver);
            Equal(0, fired);
            manualGame += .6;
            TickTimerDriver(driver);
            Equal(1, fired);
        });
        RunTimerDriver("late Unity clock binding preserves elapsed manual time and queued remaining delay", driver =>
        {
            var timer = TimerManager.Instance;
            int fired = 0;
            timer.Schedule(2, () => fired++);
            timer.Update(.3, .3);
            TickTimerDriver(driver);
            Time.timeAsDouble += 1.6;
            TickTimerDriver(driver);
            Equal(0, fired);
            Time.timeAsDouble += .2;
            TickTimerDriver(driver);
            Equal(1, fired);
        });
        RunTimerDriver("replacement service binds both Unity clocks without conflating scaled/unscaled time", driver =>
        {
            var timer = TimerManager.Instance;
            int scaled = 0, unscaled = 0;
            timer.Schedule(.5, () => scaled++);
            timer.Schedule(.5, () => unscaled++, true);
            TickTimerDriver(driver);
            Time.realtimeSinceStartupAsDouble += .6;
            TickTimerDriver(driver);
            Equal(0, scaled);
            Equal(1, unscaled);
            Time.timeAsDouble += .6;
            TickTimerDriver(driver);
            Equal(1, scaled);
            Equal(1, unscaled);
        });
        RunTimerDriver("driver rebinds a reset current instance without resurrecting disposed timer handles", driver =>
        {
            var timer = TimerManager.Instance;
            int fired = 0;
            var old = timer.Schedule(.1, () => fired += 100);
            TickTimerDriver(driver);
            timer.Dispose();
            True(!old.IsActive);
            timer.Schedule(.5, () => fired++);
            TickTimerDriver(driver);
            Time.timeAsDouble += .6;
            TickTimerDriver(driver);
            Equal(1, fired);
        });
    }

    private static void RunTimerDriver(string name, Action<MonoBehaviour> test)
    {
        Run(name, () =>
        {
            TimerManager.Release();
            Time.timeAsDouble = 100;
            Time.realtimeSinceStartupAsDouble = 1000;
            var type = typeof(MonoSingleton<>).Assembly.GetType("Game.Framework.FrameworkUpdateDriver", true);
            try
            {
                type.GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                var driver = Object.FindObjectsOfType<MonoBehaviour>(true).Single(value => value.GetType() == type);
                test(driver);
            }
            finally
            {
                type.GetMethod("Stop", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                TimerManager.Release();
            }
        });
    }

    private static void TickTimerDriver(MonoBehaviour driver)
    {
        driver.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(driver, null);
    }
}
