using System;
using UnityEngine;

namespace Game.Framework.Ads.Csj
{
    public sealed class CsjCallbackHost : MonoBehaviour
    {
        [Serializable]
        public sealed class Response
        {
            public string requestId;
            public string kind;
            public string label;
            public double amount;
        }

        internal Action<Response> Received;

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
