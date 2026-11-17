using System.Collections.Generic;
using UnityEngine;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace ECS
{
    /// <summary>提供实体的实现。</summary>
    public sealed class Entity
    {
#if UNITY_EDITOR
        public string name;
#endif

        public GameObject gameObject;

#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        public ulong GUID { get; internal set; }    // Generic Unique Identifier  本地ID

#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        public bool IsAlive { get; internal set; }

#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        internal readonly Dictionary<int, IComponent> components = new Dictionary<int, IComponent>();

        // Invalidate earlier loads even when the same live entity starts another request.
        internal ulong LoadRequestId;

        /// <summary>根据传入参数取得字符串 To 哈希。</summary>
        public static int StringToHash(string str)
        {
            int hashcode = 0;
            if (!string.IsNullOrEmpty(str))
            {
                for (int i = 0; i < str.Length; ++i)
                {
                    hashcode = hashcode * 31 + str[i];
                }
            }

            return hashcode;
        }
    }

}
