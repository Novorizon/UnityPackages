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
    /// <summary>着色器对应的管理器。管理对应模块的状态和操作入口。</summary>
    public sealed class ShaderManager : Singleton<ShaderManager>
    {
        /// <summary>调用 ResourceManager.Instance.LoadAsset&lt;ShaderVariantCollection&gt; 完成预热。</summary>
        public void Warmup(string path)
        {
            ShaderVariantCollection svc = ResourceManager.Instance.LoadAsset<ShaderVariantCollection>(path);
            if (svc != null)
            {
                svc.WarmUp();
            }
        }

        /// <summary>调用 svc.WarmUp 完成预热。</summary>
        public void Warmup(ShaderVariantCollection svc)
        {
            if (svc != null)
            {
                svc.WarmUp();
            }
        }
    }
}
