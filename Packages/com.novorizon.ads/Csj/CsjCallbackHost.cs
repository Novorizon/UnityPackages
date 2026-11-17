using System;
using UnityEngine;

namespace Game.Framework.Ads.Csj
{
    /// <summary>提供穿山甲回调宿主的实现。</summary>
    public sealed class CsjCallbackHost : MonoBehaviour
    {
        /// <summary>表示穿山甲回调宿主中的响应。</summary>
        [Serializable]
        public sealed class Response
        {
            public string requestId;
            public string kind;
            public string label;
            public double amount;
        }

        internal Action<Response> Received;

        /// <summary>响应 Novorizon 穿山甲结果。</summary>
        [UnityEngine.Scripting.Preserve]
        public void OnNovorizonCsjResult(string json)
        {
            Response response;
            try
            {
                response = JsonUtility.FromJson<Response>(json);
            }
            catch (ArgumentException)
            {
                return;
            }
            if (response != null && !string.IsNullOrEmpty(response.requestId))
            {
                Received?.Invoke(response);
            }
        }
    }
}
