#nullable disable
using System;
using UnityEngine;

namespace UI
{
    /// <summary>Internal ownership for cells belonging to one pooled virtual row.</summary>
    /// <remarks>提供界面虚拟网格行的实现。</remarks>
    public sealed class UIVirtualGridRow : MonoBehaviour
    {
        internal UIVirtualGrid Owner;
        internal GameObject[] Cells = Array.Empty<GameObject>();
        internal int[] Indices = Array.Empty<int>();
        internal Action<GameObject> Unbind;
        private int bindingVersion;
        private int releaseDepth;

        internal bool IsReleasing => releaseDepth != 0;

        /// <summary>开始绑定。</summary>
        internal void BeginBinding(Action<GameObject> callback)
        {
            bindingVersion++;
            Unbind = callback;
        }

        /// <summary>结束界面虚拟网格行对相关资源的持有，并执行该类型的清理流程。</summary>
        internal void Release(int column)
        {
            if (Indices[column] < 0)
            {
                return;
            }

            Indices[column] = -1;
            if (Cells[column] == null)
            {
                return;
            }

            try
            {
                Unbind?.Invoke(Cells[column]);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        /// <summary>释放全部。</summary>
        internal void ReleaseAll()
        {
            int version = bindingVersion;
            releaseDepth++;
            try
            {
                for (int column = 0; column < Indices.Length; column++)
                {
                    if (version != bindingVersion)
                    {
                        break;
                    }
                    Release(column);
                }
            }
            finally
            {
                releaseDepth--;
            }
        }

        /// <summary>Unity 销毁对象时执行本对象的清理逻辑。</summary>
        private void OnDestroy()
        {
            ReleaseAll();
            if (Owner != null)
            {
                Owner.Forget(this);
            }

            Owner = null;
            Unbind = null;
            Cells = Array.Empty<GameObject>();
            Indices = Array.Empty<int>();
        }
    }
}
