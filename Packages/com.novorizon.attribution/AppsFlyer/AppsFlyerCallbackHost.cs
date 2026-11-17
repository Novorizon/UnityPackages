using System;
using AppsFlyerSDK;
using UnityEngine;

namespace Game.Framework.Attribution.AppsFlyer
{
    /// <summary>提供AppsFlyer 回调宿主的实现。</summary>
    public sealed class AppsFlyerCallbackHost : MonoBehaviour, IAppsFlyerConversionData
    {
        internal Action<string> Received;

        /// <summary>调用 .Invoke 完成on 转换数据 Success。</summary>
        [UnityEngine.Scripting.Preserve]
        public void onConversionDataSuccess(string conversionData) => Received?.Invoke(conversionData);

        /// <summary>调用 .Invoke 完成on 应用实例打开归因。</summary>
        [UnityEngine.Scripting.Preserve]
        public void onAppOpenAttribution(string attributionData) => Received?.Invoke(attributionData);

        /// <summary>处理on 转换数据 Fail。</summary>
        [UnityEngine.Scripting.Preserve]
        public void onConversionDataFail(string error)
        {
            // SDK error bodies may contain identifiers. Never log or forward the raw body.
        }

        /// <summary>处理on 应用实例打开归因失败原因。</summary>
        [UnityEngine.Scripting.Preserve]
        public void onAppOpenAttributionFailure(string error)
        {
        }
    }
}
