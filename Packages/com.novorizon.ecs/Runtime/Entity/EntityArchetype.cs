using System;

namespace ECS
{
    /// <summary>保存实体原型所需的数据与配套操作。</summary>
    public struct EntityArchetype
    {
        /// <summary>使用调用方提供的typeNames创建 EntityArchetype。</summary>
        public EntityArchetype(params string[] typeNames)
        {
            if (typeNames == null) throw new ArgumentNullException(nameof(typeNames));
            this.typeNames = (string[])typeNames.Clone();
            hash = new int[typeNames.Length];
            for (int i = 0; i < typeNames.Length; ++i)
                hash[i] = ComponentType.GetId(ComponentType.Resolve(typeNames[i]));
        }

        /// <summary>使用调用方提供的types创建 EntityArchetype。</summary>
        public EntityArchetype(params Type[] types)
        {
            if (types == null) throw new ArgumentNullException(nameof(types));
            typeNames = new string[types.Length];
            hash = new int[types.Length];
            for (int i = 0; i < types.Length; ++i)
            {
                hash[i] = ComponentType.GetId(types[i]);
                typeNames[i] = types[i].AssemblyQualifiedName;
            }
        }

        public string[] typeNames;
        // Kept for source compatibility. These are ComponentType IDs, not StringToHash values.
        public int[] hash;
        public int[] ComponentTypeIds { get { return hash; } }
    }
}
