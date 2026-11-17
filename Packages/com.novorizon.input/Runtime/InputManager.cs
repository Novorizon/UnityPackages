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
    public sealed class InputManager : Singleton<InputManager>, IDisposable
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
        public bool Initialized => actions != null && !disposed;
        public InputActionAsset Actions => Initialized ? actions : throw new InvalidOperationException("Initialize InputManager first.");
        public string CurrentControlScheme { get; private set; }

        /// <summary>清理已经存在的输入管理器，未使用输入时不会创建实例。</summary>
        public static void Reset()
        {
            if (TryGetInstance(out var current))
            {
                current.Dispose();
            }
        }
        /// <summary>使用默认配置初始化输入入口。</summary>
        public override void Initialize()
        {
            Initialize(null, null);
        }

        /// <summary>复制输入资产用于本轮运行，运行时改键不会修改原始资产。</summary>
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

        /// <summary>按名称查找运行时副本中的 Action，不存在时抛出异常。</summary>
        public InputAction FindAction(string name)
        {
            Ready();
            return actions.FindAction(name, true);
        }

        /// <summary>将指定 Action Map 标记为启用。若仍有屏蔽作用域，它会在屏蔽结束后生效。</summary>
        public void EnableMap(string name)
        {
            Ready();
            var map = actions.FindActionMap(name, true);
            enabledMaps.Add(map.name);
            ApplyMap(map);
        }

        /// <summary>取消指定 Action Map 的启用请求，解除临时屏蔽后也不会恢复它。</summary>
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

        /// <summary>指定输入资产可接收的设备列表，null 取消设备限制。</summary>
        public void SetDevices(params InputDevice[] devices)
        {
            Ready();
            actions.devices = devices == null ? (ReadOnlyArray<InputDevice>? )null : new ReadOnlyArray<InputDevice>(devices);
        }

        /// <summary>嵌套屏蔽指定 Map，null 屏蔽全部；结束后只恢复仍被请求启用的 Map。</summary>
        /// <remarks>按作用域屏蔽指定 Action Map；不传名称时屏蔽全部。释放返回值才撤销这次屏蔽。</remarks>
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

        // 按屏蔽句柄对应的范围减少一层计数，再重新计算 Map 状态。
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

        /// <summary>订阅 Action 的触发或取消事件，释放返回值时撤销订阅。</summary>
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

        /// <summary>将运行时覆盖的按键配置导出为 JSON，可用于持久化。</summary>
        public string SaveBindingOverrides()
        {
            Ready();
            return actions.SaveBindingOverridesAsJson();
        }

        /// <summary>从 JSON 恢复输入绑定。交互改键进行中不能加载另一份绑定。</summary>
        public void LoadBindingOverrides(string json)
        {
            Ready();
            if (rebindingAction != null)
            {
                throw new InvalidOperationException("Cannot replace bindings during an interactive rebind.");
            }

            actions.LoadBindingOverridesFromJson(json ?? throw new ArgumentNullException(nameof(json)));
        }

        /// <summary>等待一次交互改键，结束后恢复 Action Map 状态。</summary>
        public async Task<string> RebindAsync(string actionName, int bindingIndex, CancellationToken token = default)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            if (rebindingAction != null)
            {
                throw new InvalidOperationException("An interactive rebind is already active.");
            }

            var action = FindAction(actionName);
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count || action.bindings[bindingIndex].isComposite)
            {
                throw new ArgumentOutOfRangeException(nameof(bindingIndex), "Select a simple binding or a composite part.");
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Disabling an in-progress action invokes project cancellation callbacks.
            // Reserve this slot first so those callbacks cannot start a second rebind.
            rebindingAction = action;
            InputActionRebindingExtensions.RebindingOperation operation = null;
            try
            {
                action.Disable();
                token.ThrowIfCancellationRequested();
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(InputManager));
                }
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

        // 根据已请求启用的 Map 和当前屏蔽计数统一推算实际输入状态。
        private void ApplyMaps()
        {
            foreach (var map in actions.actionMaps)
            {
                ApplyMap(map);
            }
        }

        // 将启用请求和全局、单 Map 的屏蔽状态合并应用。
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

        /// <summary>校验当前调用位于 Unity 主线程。</summary>
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

        /// <summary>调用 MainThread 完成就绪。</summary>
        private void Ready()
        {
            MainThread();
            if (!Initialized)
            {
                throw new InvalidOperationException("Initialize InputManager first.");
            }
        }

        /// <summary>结束输入管理器对相关资源的持有，并执行该类型的清理流程。</summary>
        public override void Dispose()
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

            base.Release();
        }

        /// <summary>屏蔽对应的租约。记录资源的使用权，由持有者在使用结束时归还。</summary>
        private sealed class BlockLease : IDisposable
        {
            private InputManager owner;
            private readonly string map;
            /// <summary>使用调用方提供的owner、map创建 BlockLease。</summary>
            internal BlockLease(InputManager owner, string map)
            {
                this.owner = owner;
                this.map = map;
            }

            /// <summary>结束屏蔽租约对相关资源的持有，并执行该类型的清理流程。</summary>
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

        /// <summary>表示输入管理器中的订阅。</summary>
        private sealed class Subscription : IDisposable
        {
            private InputManager owner;
            private readonly InputAction action;
            private readonly Action<InputAction.CallbackContext> performed, canceled;
            /// <summary>使用调用方提供的owner、action、performed创建 Subscription。</summary>
            internal Subscription(InputManager owner, InputAction action, Action<InputAction.CallbackContext> performed, Action<InputAction.CallbackContext> canceled)
            {
                this.owner = owner;
                this.action = action;
                this.performed = performed;
                this.canceled = canceled;
                action.performed += OnPerformed;
                action.canceled += OnCanceled;
            }

            /// <summary>响应 Performed。</summary>
            private void OnPerformed(InputAction.CallbackContext context)
            {
                if (owner != null && !owner.disposed)
                {
                    performed?.Invoke(context);
                }
            }

            /// <summary>响应 Canceled。</summary>
            private void OnCanceled(InputAction.CallbackContext context)
            {
                if (owner != null && !owner.disposed)
                {
                    canceled?.Invoke(context);
                }
            }

            /// <summary>结束订阅对相关资源的持有，并执行该类型的清理流程。</summary>
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
    }
}
