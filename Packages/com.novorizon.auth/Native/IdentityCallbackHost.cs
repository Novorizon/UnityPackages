using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Framework.Auth.Native
{
    /// <summary>Runtime-only callback receiver. No token is sent to Unity's logger.</summary>
    public sealed class IdentityCallbackHost : MonoBehaviour
    {
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

        public void FailStart()
        {
            var completion = pending;
            pending = null;
            request = null;
            completion?.TrySetException(new InvalidOperationException("The native identity request could not start."));
        }

        private void OnDestroy()
        {
            pending?.TrySetException(new ObjectDisposedException(nameof(IdentityCallbackHost)));
            pending = null;
        }
    }
}
