#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Event;
using Game.Framework.Pooling;
using Game.Framework.Timing;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Framework.Samples.Demo
{
    /// <summary>An offline dashboard. It owns only its own subscriptions, tasks, timers and pools.</summary>
    public sealed class FrameworkDemo : MonoBehaviour
    {
        enum DemoEvent
        {
            ScoreChanged
        }

        sealed class ScoreChanged : IMessage
        {
            internal readonly int Delta;
            internal ScoreChanged(int delta)
            {
                Delta = delta;
            }
        }

        readonly List<GameObject> spawned = new();
        CancellationTokenSource? lifetime;
        CancellationTokenSource? taskWaitCancellation;
        TaskCompletionSource<bool>? taskCompletion;
        ISubscription? eventSubscription;
        ReferencePool<DemoReferenceContext>? references;
        GameObjectPool? objects;
        GameObject? template;
        TimerHandle scaledTimer, unscaledTimer;
        Vector2 scroll;
        int generation;
        bool ownsPause;
        float previousTimeScale;
        public bool IsRunning { get; private set; }
        public int EventReceivedCount { get; private set; }
        public int Score { get; private set; }
        public int ScaledTicks { get; private set; }
        public int UnscaledTicks { get; private set; }
        public int ReferenceOperations { get; private set; }
        public int ReferenceCountInactive => references?.CountInactive ?? 0;
        public bool LastReferenceWasReset { get; private set; }
        public int PoolCountActive => objects?.CountActive ?? 0;
        public int PoolCountInactive => objects?.CountInactive ?? 0;
        public int MainThreadDispatchCount { get; private set; }
        public int LastWorkerThreadId { get; private set; }
        public Task? TaskWaitOperation { get; private set; }
        public string Status { get; private set; } = "Ready to start.";

        void OnEnable()
        {
            if (Application.isPlaying)
            {
                BeginDemo();
            }
        }

        public void BeginDemo()
        {
            if (IsRunning)
            {
                return;
            }

            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Run the demo after the framework's automatic Unity main-thread bootstrap.");
            }

            generation++;
            lifetime = new CancellationTokenSource();
            references = new ReferencePool<DemoReferenceContext>(maximumRetained: 8);
            references.Prewarm(4);
            EventReceivedCount = Score = ScaledTicks = UnscaledTicks = ReferenceOperations = MainThreadDispatchCount = 0;
            LastWorkerThreadId = 0;
            LastReferenceWasReset = true;
            TaskWaitOperation = null;
            IsRunning = true;
            DemoSession.Instance.RegisterVisit();
            SubscribeEvents();
            Status = "Offline demo running. No server, UI package, YooAsset or prefab is required.";
        }

        public void RestartDemo()
        {
            EndDemo();
            BeginDemo();
        }

        public void SubscribeEvents()
        {
            RequireRunning();
            eventSubscription ??= EventManager.Instance.Subscribe<DemoEvent, ScoreChanged>(DemoEvent.ScoreChanged, OnScoreChanged);
            Status = "Subscribed to the demo's typed score event.";
        }

        public void UnsubscribeEvents()
        {
            eventSubscription?.Dispose();
            eventSubscription = null;
            Status = "Unsubscribed. Publishing no longer changes this dashboard.";
        }

        public void PublishScore(int delta = 1)
        {
            RequireRunning();
            EventManager.Instance.Notify(DemoEvent.ScoreChanged, new ScoreChanged(delta));
        }

        void OnScoreChanged(ScoreChanged change)
        {
            EventReceivedCount++;
            Score += change.Delta;
        }

        public void StartTimers()
        {
            RequireRunning();
            StopTimers();
            ScaledTicks = UnscaledTicks = 0;
            scaledTimer = TimerManager.Instance.Repeat(1, OnScaledTick);
            unscaledTimer = TimerManager.Instance.Repeat(1, OnUnscaledTick, useUnscaledTime: true);
            Status = "Two one-second timers started: scaled and unscaled time.";
        }

        void OnScaledTick() => ScaledTicks++;
        void OnUnscaledTick()
        {
            UnscaledTicks++;
        }

        public void StopTimers()
        {
            CancelOwnTimers();
            Status = "This demo's timers were cancelled. Other framework timers are unchanged.";
        }

        void CancelOwnTimers()
        {
            scaledTimer.Cancel();
            unscaledTimer.Cancel();
            scaledTimer = unscaledTimer = default;
        }

        public void PauseGameTime()
        {
            RequireRunning();
            if (ownsPause)
            {
                return;
            }

            previousTimeScale = Time.timeScale;
            ownsPause = true;
            Time.timeScale = 0;
            Status = "Game time paused. The unscaled timer continues.";
        }

        public void ResumeGameTime()
        {
            if (!ownsPause)
            {
                return;
            }

            // Do not overwrite a time-scale change made by another system after this demo's pause.
            if (Time.timeScale == 0)
            {
                Time.timeScale = previousTimeScale;
            }

            ownsPause = false;
        }

        public void RentAndReturnReference()
        {
            RequireRunning();
            DemoReferenceContext value = references!.Rent();
            try
            {
                LastReferenceWasReset = value.Target == null && value.Amount == 0;
                value.Target = gameObject;
                value.Amount = 10;
                ReferenceOperations++;
            }
            finally
            {
                references.Return(value);
            }

            Status = "Reference object rented, used and returned in finally; Reset removes its GameObject reference.";
        }

        public void SpawnObject()
        {
            RequireRunning();
            RemoveDestroyedSpawns();
            if (spawned.Count >= 16)
            {
                Status = "The demo caps active objects at 16. Despawn some first.";
                return;
            }

            if (objects == null)
            {
                template = new GameObject("Core Demo Template");
                template.SetActive(false);
                template.transform.SetParent(transform, false);
                template.AddComponent<CoreDemoPooledItem>();
                objects = new GameObjectPool(template, maximumRetained: 8, prewarm: 4);
            }

            GameObject instance = objects.Spawn(transform);
            instance.name = "Core Demo Pooled Item " + (spawned.Count + 1);
            spawned.Add(instance);
            Status = "Spawned an empty marker GameObject. Inspect its pool callbacks and reuse in the Hierarchy.";
        }

        public void DespawnObject()
        {
            RequireRunning();
            RemoveDestroyedSpawns();
            if (spawned.Count == 0)
            {
                return;
            }

            int last = spawned.Count - 1;
            GameObject instance = spawned[last];
            spawned.RemoveAt(last);
            objects!.Despawn(instance);
        }

        public void DespawnAll()
        {
            RequireRunning();
            while (spawned.Count > 0)
            {
                DespawnObject();
            }
        }

        void RemoveDestroyedSpawns()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                if (spawned[i] == null)
                {
                    spawned.RemoveAt(i);
                }
            }
        }

        public Task DispatchWorkerAsync()
        {
            RequireRunning();
            int currentGeneration = generation;
            CancellationToken token = lifetime!.Token;
            // No Unity API is accessed on the worker. The returned Task represents the main-thread action too.
            Task operation = Task.Run(async () =>
            {
                int worker = Thread.CurrentThread.ManagedThreadId;
                await UnityThread.RunAsync(() =>
                {
                    if (!OwnsSession(currentGeneration, token))
                    {
                        return;
                    }

                    LastWorkerThreadId = worker;
                    MainThreadDispatchCount++;
                    Status = "Worker " + worker + " delivered a callback on main thread " + Thread.CurrentThread.ManagedThreadId + ".";
                }, token).ConfigureAwait(false);
            }, token);
            ObserveFault(operation);
            return operation;
        }

        public Task StartTaskWait()
        {
            RequireRunning();
            CancelTaskWait();
            taskWaitCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime!.Token);
            taskCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task operation = TaskUtil.WaitAsync(taskCompletion.Task, taskWaitCancellation.Token);
            TaskWaitOperation = operation;
            ObserveFault(operation);
            Status = "TaskUtil is waiting. Complete or cancel it with the adjacent buttons.";
            return operation;
        }

        public void CompleteTaskWait() => taskCompletion?.TrySetResult(true);
        public void CancelTaskWait()
        {
            CancellationTokenSource? cancellation = taskWaitCancellation;
            TaskCompletionSource<bool>? completion = taskCompletion;
            Task? operation = TaskWaitOperation;
            taskWaitCancellation = null;
            taskCompletion = null;
            try
            {
                cancellation?.Cancel();
            }
            finally
            {
                // Finish the synthetic work only after its waiter has observed cancellation.
                // Racing SetResult with Cancel would make the Cancel button nondeterministic.
                if (completion != null && operation != null)
                {
                    _ = operation.ContinueWith(_ => completion.TrySetResult(false), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                else
                {
                    completion?.TrySetResult(false);
                }

                cancellation?.Dispose();
            }
        }

        static void ObserveFault(Task operation)
        {
            _ = operation.ContinueWith(completed =>
            {
                _ = completed.Exception;
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public void WriteLog()
        {
            RequireRunning();
            Game.Framework.Logger.Info("Core Demo: Info requires ENABLE_DEBUG_LOG.");
            Game.Framework.Logger.Warn("Core Demo: this is an intentional warning from the logging example.");
            Status = "Check the Console. Warn is always emitted; Info is conditional on ENABLE_DEBUG_LOG.";
        }

        public void EndDemo()
        {
            if (!IsRunning)
            {
                return;
            }

            // Teardown must not depend on an old absolute clock after a new Play session.
            CancelOwnTimers();
            ResumeGameTime();
            IsRunning = false;
            generation++;
            CancellationTokenSource? cancellation = lifetime;
            lifetime = null;
            try
            {
                cancellation?.Cancel();
            }
            finally
            {
                cancellation?.Dispose();
            }

            CancelTaskWait();
            UnsubscribeEvents();
            GameObjectPool? ownedPool = objects;
            objects = null;
            spawned.Clear();
            ownedPool?.Dispose();
            references?.Clear();
            references = null;
            if (template != null)
            {
                Object.Destroy(template);
            }

            template = null;
            Status = "Demo stopped. Only this demo's subscriptions, timers, tasks and pools were cleaned.";
        }

        bool OwnsSession(int expectedGeneration, CancellationToken token) => IsRunning && generation == expectedGeneration && !token.IsCancellationRequested;
        void RequireRunning()
        {
            if (!IsRunning)
            {
                throw new InvalidOperationException("Start the demo first.");
            }
        }

        void OnDisable() => EndDemo();
        void OnDestroy() => EndDemo();
        void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Status = exception.Message;
                Game.Framework.Logger.Exception(exception);
            }
        }

        void OnGUI()
        {
            float width = Math.Max(240, Math.Min(Screen.width - 24, 760));
            GUILayout.BeginArea(new Rect(12, 12, width, Math.Max(120, Screen.height - 24)), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Novorizon Framework - Offline Core Demo");
            GUILayout.Label(Status);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start / restart"))
            {
                Run(RestartDemo);
            }

            if (GUILayout.Button("Stop and clean up"))
            {
                Run(EndDemo);
            }

            GUILayout.EndHorizontal();
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && IsRunning;
            GUILayout.Label("Typed events | received " + EventReceivedCount + " | score " + Score);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Subscribe"))
            {
                Run(SubscribeEvents);
            }

            if (GUILayout.Button("Publish +1"))
            {
                Run(() => PublishScore());
            }

            if (GUILayout.Button("Unsubscribe"))
            {
                Run(UnsubscribeEvents);
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("Timers | scaled " + ScaledTicks + " | unscaled " + UnscaledTicks + " | timeScale " + Time.timeScale);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start timers"))
            {
                Run(StartTimers);
            }

            if (GUILayout.Button("Cancel timers"))
            {
                Run(StopTimers);
            }

            if (GUILayout.Button("Pause game time"))
            {
                Run(PauseGameTime);
            }

            if (GUILayout.Button("Resume"))
            {
                Run(ResumeGameTime);
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("Reference pool | operations " + ReferenceOperations + " | cached " + ReferenceCountInactive + " | reset verified " + LastReferenceWasReset);
            if (GUILayout.Button("Rent / use / return"))
            {
                Run(RentAndReturnReference);
            }

            GUILayout.Label("GameObject pool | active " + PoolCountActive + " | cached " + PoolCountInactive);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn marker"))
            {
                Run(SpawnObject);
            }

            if (GUILayout.Button("Despawn last"))
            {
                Run(DespawnObject);
            }

            if (GUILayout.Button("Despawn all"))
            {
                Run(DespawnAll);
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("Thread dispatch | main callbacks " + MainThreadDispatchCount + " | last worker " + LastWorkerThreadId);
            if (GUILayout.Button("Worker -> UnityThread.RunAsync"))
            {
                Run(() =>
                {
                    _ = DispatchWorkerAsync();
                });
            }

            GUILayout.Label("TaskUtil | " + (TaskWaitOperation?.Status.ToString() ?? "not started"));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start wait"))
            {
                Run(() =>
                {
                    _ = StartTaskWait();
                });
            }

            if (GUILayout.Button("Complete"))
            {
                Run(CompleteTaskWait);
            }

            if (GUILayout.Button("Cancel wait"))
            {
                Run(CancelTaskWait);
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("Singleton DemoSession | visits this Play: " + DemoSession.Instance.Visits);
            if (GUILayout.Button("Write example log"))
            {
                Run(WriteLog);
            }

            GUILayout.Label("Network: import TCP Network separately; connections require explicit credentials and configured server settings.");
            GUI.enabled = previousEnabled;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
