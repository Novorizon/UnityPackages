///------------------------------------
/// Author：guanjinbiao
/// Mail：novogooglor@gmail.com
/// Date：2025-12-10
/// Description：shader预热
///------------------------------------
using Game.Framework;
using UnityEngine;

namespace Game.Resource
{
    public sealed class ShaderManager : Singleton<ShaderManager>
    {
        public void Warmup(string path)
        {
            ShaderVariantCollection svc = ResourceManager.Instance.LoadAsset<ShaderVariantCollection>(path);
            if (svc != null)
            {
                svc.WarmUp();
            }
        }

        public void Warmup(ShaderVariantCollection svc)
        {
            if (svc != null)
            {
                svc.WarmUp();
            }
        }
    }
}
