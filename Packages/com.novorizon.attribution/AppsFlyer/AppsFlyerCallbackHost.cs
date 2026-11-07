using System;
using AppsFlyerSDK;
using UnityEngine;

namespace Game.Framework.Attribution.AppsFlyer
{
    public sealed class AppsFlyerCallbackHost : MonoBehaviour, IAppsFlyerConversionData
    {
        internal Action<string> Received;

        [UnityEngine.Scripting.Preserve]
        public void onConversionDataSuccess(string conversionData) => Received?.Invoke(conversionData);

        [UnityEngine.Scripting.Preserve]
        public void onAppOpenAttribution(string attributionData) => Received?.Invoke(attributionData);

        [UnityEngine.Scripting.Preserve]
        public void onConversionDataFail(string error)
        {
            // SDK error bodies may contain identifiers. Never log or forward the raw body.
        }

        [UnityEngine.Scripting.Preserve]
        public void onAppOpenAttributionFailure(string error)
        {
        }
    }
}
