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
            var variants = ResourceManager.LoadAsset<ShaderVariantCollection>(path);
            if (variants == null)
            {
                return;
            }

            try
            {
                variants.WarmUp();
            }
            finally
            {
                ResourceManager.Release(variants);
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
