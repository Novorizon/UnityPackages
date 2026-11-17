using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Framework.Auth.Native
{
    /// <summary>Runtime-only callback receiver. No token is sent to Unity's logger.</summary>
    /// <remarks>提供身份回调宿主的实现。</remarks>
    public sealed class IdentityCallbackHost : MonoBehaviour
    {
        /// <summary>表示身份回调宿主中的响应。</summary>
        [Serializable]
        public sealed class Response
        {
            public string requestId;
            public string token;
            public string accessToken;
            public string error;
            public bool cancelled;
        }

        private TaskCompletionSource<Response> pending;
        private string request;

        /// <summary>开始身份回调宿主。</summary>
        public Task<Response> Begin(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException("A native identity request ID is required.", nameof(requestId));
            }
            if (pending != null)
            {
                throw new InvalidOperationException("A native identity request is already active.");
            }
            request = requestId;
            pending = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
            return pending.Task;
        }

        /// <summary>响应原生身份结果。</summary>
        [UnityEngine.Scripting.Preserve]
        public void OnNativeIdentityResult(string json)
        {
            Response value;
            try
            {
                value = JsonUtility.FromJson<Response>(json);
            }
            catch (ArgumentException)
            {
                // Do not log a malformed payload: it may still contain a provider token.
                return;
            }
            if (value == null || value.requestId != request || pending == null)
            {
                return;
            }
            var completion = pending;
            pending = null;
            request = null;
            completion.TrySetResult(value);
        }

        /// <summary>标记失败 Start。</summary>
        public void FailStart()
        {
            var completion = pending;
            pending = null;
            request = null;
            completion?.TrySetException(new InvalidOperationException("The native identity request could not start."));
        }

        /// <summary>Unity 销毁对象时执行本对象的清理逻辑。</summary>
        private void OnDestroy()
        {
            pending?.TrySetException(new ObjectDisposedException(nameof(IdentityCallbackHost)));
            pending = null;
        }
    }
}
