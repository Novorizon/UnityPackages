using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace Game.Framework.Input
{
    /// <summary>管理独立的运行时 Action Asset 副本；所有控制 API 在 Unity 主线程调用。</summary>
    public sealed class InputManager : ConfigurableSingleton<InputManager>, IDisposable
    {
        private readonly HashSet<string> enabledMaps = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> mapBlocks = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<Subscription> subscriptions = new HashSet<Subscription>();
        private InputActionAsset actions;
        private InputActionRebindingExtensions.RebindingOperation rebinding;
        private InputAction rebindingAction;
        private int allBlocks;
        private int ownerThread;
        private bool disposed;
        public static InputManager Instance => GetOrCreateInstance(() => new InputManager());
        public bool Initialized => actions != null && !disposed;
        public InputActionAsset Actions => Initialized ? actions : throw new InvalidOperationException("Initialize InputManager first.");
        public string CurrentControlScheme { get; private set; }

        public static void Reset(bool stopping = false) => ResetInstance(value => value.Dispose(), stopping);
        public void Initialize(InputActionAsset source = null, string initialMap = null)
        {
            MainThread();
            if (Initialized)
            {
                throw new InvalidOperationException("InputManager is already initialized; dispose it before changing assets.");
            }

            ownerThread = Thread.CurrentThread.ManagedThreadId;
            var clone = source != null ? UnityEngine.Object.Instantiate(source) : ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                clone.Disable();
                actions = clone;
                if (!string.IsNullOrEmpty(initialMap))
                {
                    EnableMap(initialMap);
                }
            }
            catch
            {
                actions = null;
                enabledMaps.Clear();
                UnityEngine.Object.Destroy(clone);
                throw;
            }
        }

        public InputAction FindAction(string name)
        {
            Ready();
            return actions.FindAction(name, true);
        }

        public void EnableMap(string name)
        {
            Ready();
            var map = actions.FindActionMap(name, true);
            enabledMaps.Add(map.name);
            ApplyMap(map);
        }

        public void DisableMap(string name)
        {
            Ready();
            var map = actions.FindActionMap(name, true);
            enabledMaps.Remove(map.name);
            ApplyMap(map);
        }

        /// <summary>独占切换：关闭其他 Map；需要 UI/Gameplay 同时工作时分别调用 EnableMap。</summary>
        public void SwitchMap(string name)
        {
            Ready();
            var target = actions.FindActionMap(name, true);
            enabledMaps.Clear();
            enabledMaps.Add(target.name);
            ApplyMaps();
        }

        /// <summary>只改变此管理器的 binding group 过滤，不接管项目的 PlayerInput 或多人设备配对。</summary>
        public void SetControlScheme(string name)
        {
            Ready();
            if (string.IsNullOrEmpty(name))
            {
                actions.bindingMask = null;
                CurrentControlScheme = null;
                return;
            }

            int index = actions.FindControlSchemeIndex(name);
            if (index < 0)
            {
                throw new ArgumentException("Unknown control scheme: " + name, nameof(name));
            }

            actions.bindingMask = InputBinding.MaskByGroup(actions.controlSchemes[index].bindingGroup);
            CurrentControlScheme = name;
        }

        public void SetDevices(params InputDevice[] devices)
        {
            Ready();
            actions.devices = devices == null ? (ReadOnlyArray<InputDevice>? )null : new ReadOnlyArray<InputDevice>(devices);
        }

        /// <summary>嵌套屏蔽指定 Map，null 屏蔽全部；结束后只恢复仍被请求启用的 Map。</summary>
        public IDisposable Block(string mapName = null)
        {
            Ready();
            if (mapName == null)
            {
                allBlocks++;
            }
            else
            {
                mapName = actions.FindActionMap(mapName, true).name;
                mapBlocks.TryGetValue(mapName, out int count);
                mapBlocks[mapName] = count + 1;
            }

            ApplyMaps();
            return new BlockLease(this, mapName);
        }

        private void Unblock(string mapName)
        {
            if (disposed)
            {
                return;
            }

            Ready();
            if (mapName == null)
            {
                allBlocks--;
            }
            else
            {
                int count = mapBlocks[mapName] - 1;
                if (count == 0)
                {
                    mapBlocks.Remove(mapName);
                }
                else
                {
                    mapBlocks[mapName] = count;
                }
            }

            ApplyMaps();
        }

        public IDisposable Subscribe(string action, Action<InputAction.CallbackContext> performed, Action<InputAction.CallbackContext> canceled = null)
        {
            Ready();
            if (performed == null && canceled == null)
            {
                throw new ArgumentException("At least one input callback is required.");
            }

            var subscription = new Subscription(this, FindAction(action), performed, canceled);
            subscriptions.Add(subscription);
            return subscription;
        }

        public string SaveBindingOverrides()
        {
            Ready();
            return actions.SaveBindingOverridesAsJson();
        }

        public void LoadBindingOverrides(string json)
        {
            Ready();
            if (rebinding != null)
            {
                throw new InvalidOperationException("Cannot replace bindings during an interactive rebind.");
            }

            actions.LoadBindingOverridesFromJson(json ?? throw new ArgumentNullException(nameof(json)));
        }

        public async Task<string> RebindAsync(string actionName, int bindingIndex, CancellationToken token = default)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            if (rebinding != null)
            {
                throw new InvalidOperationException("An interactive rebind is already active.");
            }

            var action = FindAction(actionName);
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count || action.bindings[bindingIndex].isComposite)
            {
                throw new ArgumentOutOfRangeException(nameof(bindingIndex), "Select a simple binding or a composite part.");
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            action.Disable();
            rebindingAction = action;
            InputActionRebindingExtensions.RebindingOperation operation = null;
            try
            {
                operation = action.PerformInteractiveRebinding(bindingIndex);
                rebinding = operation;
                operation.WithCancelingThrough("<Keyboard>/escape").WithControlsExcluding("<Mouse>/position").WithControlsExcluding("<Mouse>/delta").OnComplete(_ => completion.TrySetResult(true)).OnCancel(_ => completion.TrySetCanceled());
                operation.Start();
                await TaskUtil.WaitAsync(completion.Task, token);
                token.ThrowIfCancellationRequested();
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(InputManager));
                }

                return SaveBindingOverrides();
            }
            finally
            {
                // 取消从 Task 续体回到主线程，不能在后台 CancellationToken 回调中操作 Input System。
                operation?.Cancel();
                operation?.Dispose();
                if (ReferenceEquals(rebinding, operation))
                {
                    rebinding = null;
                    rebindingAction = null;
                }

                if (!disposed)
                {
                    ApplyMaps();
                }
            }
        }

        private void ApplyMaps()
        {
            foreach (var map in actions.actionMaps)
            {
                ApplyMap(map);
            }
        }

        private void ApplyMap(InputActionMap map)
        {
            if (enabledMaps.Contains(map.name) && allBlocks == 0 && !mapBlocks.ContainsKey(map.name))
            {
                map.Enable();
            }
            else
            {
                map.Disable();
            }

            rebindingAction?.Disable();
        }

        private void MainThread()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(InputManager));
            }

            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("InputManager must run on Unity's initialized main thread.");
            }
        }

        private void Ready()
        {
            MainThread();
            if (!Initialized)
            {
                throw new InvalidOperationException("Initialize InputManager first.");
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (ownerThread != 0 && ownerThread != Thread.CurrentThread.ManagedThreadId)
            {
                throw new InvalidOperationException("Dispose InputManager on its owning thread.");
            }

            disposed = true;
            foreach (var subscription in new List<Subscription>(subscriptions))
            {
                subscription.Dispose();
            }

            rebinding?.Cancel();
            if (actions != null)
            {
                actions.Disable();
                UnityEngine.Object.Destroy(actions);
                actions = null;
            }

            ReleaseInstance(this);
        }

        private sealed class BlockLease : IDisposable
        {
            private InputManager owner;
            private readonly string map;
            internal BlockLease(InputManager owner, string map)
            {
                this.owner = owner;
                this.map = map;
            }

            public void Dispose()
            {
                var current = owner;
                if (current == null)
                {
                    return;
                }

                current.Unblock(map);
                owner = null;
            }
        }

        private sealed class Subscription : IDisposable
        {
            private InputManager owner;
            private readonly InputAction action;
            private readonly Action<InputAction.CallbackContext> performed, canceled;
            internal Subscription(InputManager owner, InputAction action, Action<InputAction.CallbackContext> performed, Action<InputAction.CallbackContext> canceled)
            {
                this.owner = owner;
                this.action = action;
                this.performed = performed;
                this.canceled = canceled;
                action.performed += OnPerformed;
                action.canceled += OnCanceled;
            }

            private void OnPerformed(InputAction.CallbackContext context)
            {
                if (owner != null && !owner.disposed)
                {
                    performed?.Invoke(context);
                }
            }

            private void OnCanceled(InputAction.CallbackContext context)
            {
                if (owner != null && !owner.disposed)
                {
                    canceled?.Invoke(context);
                }
            }

            public void Dispose()
            {
                if (owner == null)
                {
                    return;
                }

                action.performed -= OnPerformed;
                action.canceled -= OnCanceled;
                owner.subscriptions.Remove(this);
                owner = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay()
        {
            Reset();
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting() => Reset(true);
    }
}
