#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    /// <summary>提供界面视图的实现。</summary>
    public abstract class UIView : MonoBehaviour
    {
        /// <summary>表示界面视图中的保护条件请求。</summary>
        sealed class GuardRequest
        {
            readonly object gate = new();
            readonly List<GuardRequest> pending;
            CancellationTokenSource? cancellation;
            int waiters, cancellationOperations;
            bool completed, cancelling;
            internal readonly Func<UICloseReason, CancellationToken, Task<bool>> Guard;
            internal readonly UICloseReason Reason;
            internal readonly CancellationToken Token;
            internal readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            /// <summary>使用调用方提供的guard、reason、scopeToken创建 GuardRequest。</summary>
            internal GuardRequest(Func<UICloseReason, CancellationToken, Task<bool>> guard, UICloseReason reason,
                CancellationToken scopeToken, List<GuardRequest> pending)
            {
                Guard = guard; Reason = reason; this.pending = pending;
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(scopeToken, CancellationToken.None);
                Token = cancellation.Token;
            }
            /// <summary>尝试添加等待方，返回是否成功。</summary>
            internal bool TryAddWaiter(out GuardWaiter? waiter)
            {
                lock (gate)
                {
                    if (completed || cancelling || Token.IsCancellationRequested) { waiter = null; return false; }
                    waiters++; waiter = new GuardWaiter(this); return true;
                }
            }
            /// <summary>移除等待方。</summary>
            internal void RemoveWaiter()
            {
                CancellationTokenSource? source = null;
                lock (gate)
                {
                    waiters--;
                    if (waiters == 0 && !completed && !cancelling)
                    {
                        cancelling = true; cancellationOperations++;
                        source = cancellation;
                    }
                }
                if (source == null) return;
                RemovePending();
                // User cancellation callbacks run outside the gate, and can synchronously reenter the view.
                try { source.Cancel(); }
                catch (Exception exception) { Debug.LogException(exception); }
                finally
                {
                    CancellationTokenSource? dispose = null;
                    lock (gate)
                    {
                        cancellationOperations--;
                        if (completed && cancellationOperations == 0) { dispose = cancellation; cancellation = null; }
                    }
                    dispose?.Dispose();
                }
            }
            /// <summary>完成保护条件请求。</summary>
            internal void Complete(bool approved, Exception? failure)
            {
                CancellationTokenSource? dispose = null;
                lock (gate)
                {
                    if (completed) return;
                    completed = true;
                    if (cancellationOperations == 0) { dispose = cancellation; cancellation = null; }
                }
                RemovePending();
                dispose?.Dispose();
                if (failure != null) Completion.TrySetException(failure);
                else Completion.TrySetResult(approved);
            }
            /// <summary>移除待处理。</summary>
            void RemovePending() { lock (pending) pending.Remove(this); }
        }
        /// <summary>表示界面视图中的保护条件等待方。</summary>
        sealed class GuardWaiter : IDisposable
        {
            GuardRequest? owner;
            /// <summary>使用调用方提供的owner创建 GuardWaiter。</summary>
            internal GuardWaiter(GuardRequest owner) { this.owner = owner; }
            /// <summary>结束保护条件等待方对相关资源的持有，并执行该类型的清理流程。</summary>
            public void Dispose() => Interlocked.Exchange(ref owner, null)?.RemoveWaiter();
        }
        bool destroyed;
        bool shown;
        bool closeRecorded;
        bool hasCallbackScope;
        long operation;
        UILifetimeScope? openScope;
        UILifetimeScope? destroyScope;
        UILifetimeScope? callbackScope;
        List<GuardRequest>? closeConfirmations;

        public bool IsOpen { get; private set; }
        public bool IsVisible { get; private set; }
        public UICloseReason LastCloseReason { get; private set; } = UICloseReason.Programmatic;
        public Func<UICloseReason, CancellationToken, Task<bool>>? CloseGuard { get; set; }
        public CancellationToken OpenToken => CurrentTrackingScope?.Token ?? new CancellationToken(true);
        public CancellationToken DestroyToken => destroyed ? new CancellationToken(true) : (destroyScope ??= new UILifetimeScope()).Token;
        internal bool IsDestroyed => destroyed;
        internal int InstanceId { get; set; }
        internal event Action<UIView>? Closed;
        internal event Action<UIView>? Destroyed;

        UILifetimeScope? CurrentTrackingScope => hasCallbackScope ? callbackScope : IsOpen ? openScope : null;

        /// <summary>跟踪界面视图。</summary>
        protected T Track<T>(T subscription) where T : IDisposable
        {
            UILifetimeScope? scope = CurrentTrackingScope;
            if (scope != null) return scope.Track(subscription);
            return DisposeUntracked(subscription);
        }
        /// <summary>跟踪 Until 已销毁。</summary>
        protected T TrackUntilDestroyed<T>(T subscription) where T : IDisposable
        {
            if (!destroyed) return (destroyScope ??= new UILifetimeScope()).Track(subscription);
            return DisposeUntracked(subscription);
        }

        /// <summary>释放 Untracked。</summary>
        static T DisposeUntracked<T>(T subscription) where T : IDisposable
        {
            if (subscription is null) throw new ArgumentNullException(nameof(subscription));
            try { subscription.Dispose(); }
            catch (Exception exception) { Debug.LogException(exception); }
            return subscription;
        }

        /// <summary>调用 OnCreate 完成内部 OnCreate。</summary>
        internal void InternalOnCreate() => OnCreate();

        /// <summary>调用 DisposeScope 完成内部 On 打开。</summary>
        internal void InternalOnOpen(object? args)
        {
            if (destroyed) return;
            int id = InstanceId;
            long version = ++operation;
            UILifetimeScope? previous = openScope;
            openScope = null;
            closeConfirmations = null;
            IsOpen = false;
            DisposeScope(previous);
            if (destroyed || !OwnsOperation(id, version)) return;

            var scope = new UILifetimeScope();
            openScope = scope; IsOpen = true; closeRecorded = false;
            InvokeInScope(scope, () =>
            {
                if (this is IUIResultSource source) source.PrepareResult();
                if (!OwnsOpen(id, version, scope)) return;
                object? businessArgs = args;
                if (args is IUIOpenContext context)
                {
                    context.Prepared(this);
                    if (!OwnsOpen(id, version, scope)) return;
                    businessArgs = context.Args;
                    if (!OwnsOpen(id, version, scope)) return;
                }
                OnOpen(businessArgs);
                if (OwnsOpen(id, version, scope)) NotifyShown();
            });
        }

        /// <summary>调用 gameObject.SetActive 完成内部集合可见。</summary>
        internal void InternalSetVisible(bool visible)
        {
            if (destroyed) return;
            int id = InstanceId; long version = operation;
            UILifetimeScope? scope = openScope;
            bool notifyHide = !visible && shown;
            IsVisible = visible;
            if (!visible) shown = false;
            // Keep native activation separate from OnOpen; OnEnable may close or replace this lease.
            gameObject.SetActive(visible);
            if (!OwnsOperation(id, version) || !ReferenceEquals(scope, openScope)) return;
            if (visible) NotifyShown();
            else if (notifyHide) InvokeInScope(scope, OnHide);
        }
        /// <summary>通知已显示。</summary>
        void NotifyShown()
        {
            if (!IsOpen || !IsVisible || shown || destroyed) return;
            shown = true;
            InvokeInScope(openScope, OnShow);
        }

        /// <summary>调用 DisposeScope 完成内部 OnClose。</summary>
        internal void InternalOnClose(UICloseReason reason = UICloseReason.Programmatic)
        {
            int id = InstanceId; long version = ++operation;
            bool wasOpen = IsOpen, wasShown = shown;
            UILifetimeScope? previous = openScope;
            openScope = null; closeConfirmations = null;
            IsOpen = false; IsVisible = false; shown = false;
            if (wasOpen || !closeRecorded) LastCloseReason = reason;
            closeRecorded = true;
            DisposeScope(previous);
            Exception? failure = null;
            if (OwnsOperation(id, version) && wasShown) TryInvoke(previous, OnHide, ref failure);
            if (OwnsOperation(id, version) && wasOpen) TryInvoke(previous, OnClose, ref failure);
            if (OwnsOperation(id, version) && wasOpen) InvokeEvent(Closed, id, version, ref failure);
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        /// <summary>返回内部 CanClose的判断结果。</summary>
        internal bool InternalCanClose(UICloseReason reason)
        {
            if (!CanUseCurrentScope() || CloseGuard != null) return false;
            int id = InstanceId; long version = operation; var scope = openScope!;
            bool allowed = CanClose(reason);
            return allowed && OwnsOpen(id, version, scope);
        }
        /// <summary>根据传入参数取得内部 CanCloseAsync。</summary>
        internal Task<bool> InternalCanCloseAsync(UICloseReason reason, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<bool>(cancellationToken);
            if (!CanUseCurrentScope()) return Task.FromResult(false);
            int id = InstanceId; long version = operation; var scope = openScope!;
            bool allowed = CanClose(reason);
            if (!allowed || !OwnsOpen(id, version, scope)) return Task.FromResult(false);
            var guard = CloseGuard;
            if (guard == null) return Task.FromResult(true);
            List<GuardRequest> pending = closeConfirmations ??= new List<GuardRequest>();
            GuardRequest? request = null;
            GuardWaiter? waiter = null;
            bool start = false;
            lock (pending)
            {
                foreach (GuardRequest candidate in pending)
                {
                    if (ReferenceEquals(candidate.Guard, guard) && candidate.Reason == reason && candidate.TryAddWaiter(out waiter))
                    { request = candidate; break; }
                }
                if (request == null)
                {
                    request = new GuardRequest(guard, reason, scope.Token, pending);
                    if (request.TryAddWaiter(out waiter)) { pending.Add(request); start = true; }
                }
            }
            if (waiter == null)
            {
                request.Complete(false, null);
                return cancellationToken.IsCancellationRequested ? Task.FromCanceled<bool>(cancellationToken) : Task.FromResult(false);
            }
            // Register this caller before invoking a guard that can synchronously cancel or reenter.
            Task<bool> approval = AwaitApprovalAsync(request.Completion.Task, waiter!, guard, scope, id, version, reason, cancellationToken);
            if (start) _ = ConfirmCloseAsync(request, guard, reason, scope);
            return approval;
        }
        /// <summary>检查是否可以设置并使用当前作用域。</summary>
        bool CanUseCurrentScope() => !destroyed && IsOpen && openScope != null &&
            (!hasCallbackScope || ReferenceEquals(callbackScope, openScope));
        /// <summary>异步确认 Close。</summary>
        async Task ConfirmCloseAsync(GuardRequest request, Func<UICloseReason, CancellationToken, Task<bool>> guard,
            UICloseReason reason, UILifetimeScope scope)
        {
            bool approved = false;
            Exception? failure = null;
            try
            {
                request.Token.ThrowIfCancellationRequested();
                Task<bool>? task = null;
                InvokeInScope(scope, () => task = guard(reason, request.Token));
                if (task == null) throw new InvalidOperationException("CloseGuard must return a Task.");
                approved = await WaitForCancellationAsync(task, request.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { failure = exception; }
            request.Complete(approved, failure);
        }
        /// <summary>异步等待 Approval。</summary>
        async Task<bool> AwaitApprovalAsync(Task<bool> task, GuardWaiter waiter, Func<UICloseReason, CancellationToken, Task<bool>> guard,
            UILifetimeScope scope, int id, long version, UICloseReason reason, CancellationToken caller)
        {
            try
            {
                using (caller.Register(static state => ((GuardWaiter)state!).Dispose(), waiter))
                {
                    bool approved;
                    try { approved = await WaitForCancellationAsync(task, caller); }
                    catch (OperationCanceledException) when (!caller.IsCancellationRequested) { return false; }
                    caller.ThrowIfCancellationRequested();
                    if (!approved || !ReferenceEquals(CloseGuard, guard) || !OwnsOpen(id, version, scope)) return false;
                    bool allowed = CanClose(reason);
                    return allowed && OwnsOpen(id, version, scope);
                }
            }
            finally { waiter.Dispose(); }
        }
        /// <summary>异步等待 For 取消。</summary>
        static async Task<bool> WaitForCancellationAsync(Task<bool> task, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                ObserveFault(task);
                token.ThrowIfCancellationRequested();
            }
            if (task.IsCompleted || !token.CanBeCanceled) return await task;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelled.TrySetResult(false)))
            {
                if (await Task.WhenAny(task, cancelled.Task) != task)
                {
                    ObserveFault(task);
                    token.ThrowIfCancellationRequested();
                }
                return await task;
            }
        }
        /// <summary>观察故障。</summary>
        static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        /// <summary>返回持有操作的判断结果。</summary>
        bool OwnsOperation(int id, long version) => InstanceId == id && operation == version;
        /// <summary>返回持有打开的判断结果。</summary>
        bool OwnsOpen(int id, long version, UILifetimeScope scope) => !destroyed && IsOpen &&
            OwnsOperation(id, version) && ReferenceEquals(openScope, scope) && !scope.IsDisposed;
        /// <summary>释放作用域。</summary>
        void DisposeScope(UILifetimeScope? scope)
        {
            if (scope != null) InvokeInScope(scope, scope.Dispose);
        }
        /// <summary>调用 callback 完成InvokeIn 作用域。</summary>
        void InvokeInScope(UILifetimeScope? scope, Action callback)
        {
            bool previousFlag = hasCallbackScope; var previousScope = callbackScope;
            hasCallbackScope = true; callbackScope = scope;
            try { callback(); }
            finally { hasCallbackScope = previousFlag; callbackScope = previousScope; }
        }
        /// <summary>尝试处理 Invoke。</summary>
        void TryInvoke(UILifetimeScope? scope, Action callback, ref Exception? failure)
        {
            try { InvokeInScope(scope, callback); }
            catch (Exception exception) { RecordFailure(exception, ref failure); }
        }
        /// <summary>调用 callbacks.GetInvocationList 完成Invoke 事件。</summary>
        void InvokeEvent(Action<UIView>? callbacks, int id, long version, ref Exception? failure)
        {
            if (callbacks == null) return;
            foreach (Action<UIView> callback in callbacks.GetInvocationList())
            {
                if (!OwnsOperation(id, version)) break;
                try { callback(this); }
                catch (Exception exception) { RecordFailure(exception, ref failure); }
            }
        }
        /// <summary>记录失败原因。</summary>
        static void RecordFailure(Exception exception, ref Exception? failure)
        {
            if (failure == null) failure = exception;
            else Debug.LogException(exception);
        }

        // Override OnDestroyed for cleanup; Unity's OnDestroy message belongs to the framework.
        /// <summary>Unity 销毁对象时执行本对象的清理逻辑。</summary>
        protected void OnDestroy()
        {
            if (destroyed) return;
            destroyed = true;
            Exception? failure = null;
            try { InternalOnClose(UICloseReason.Destroyed); }
            catch (Exception exception) { RecordFailure(exception, ref failure); }
            UILifetimeScope? lifetime = destroyScope; destroyScope = null;
            DisposeScope(lifetime);
            TryInvoke(lifetime, OnDestroyed, ref failure);
            int id = InstanceId; long version = operation;
            try { InvokeEvent(Destroyed, id, version, ref failure); }
            finally { InstanceId = 0; Destroyed = null; Closed = null; CloseGuard = null; }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        /// <summary>检查是否可以关闭。</summary>
        protected virtual bool CanClose(UICloseReason reason) => true;
        /// <summary>响应 Create。</summary>
        protected virtual void OnCreate() { }
        /// <summary>响应打开。</summary>
        protected virtual void OnOpen(object? args) { }
        /// <summary>响应 Show。</summary>
        protected virtual void OnShow() { }
        /// <summary>响应 Hide。</summary>
        protected virtual void OnHide() { }
        /// <summary>响应 Close。</summary>
        protected virtual void OnClose() { }
        /// <summary>响应已销毁。</summary>
        protected virtual void OnDestroyed() { }
    }
}
