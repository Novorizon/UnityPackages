// ECS 入口编译边界；实体/世界实现的原生行为不由本工具模拟。
using System;
using System.Collections.Generic;

namespace ECS
{
    public sealed class World
    {
    }

    public sealed class WorldManager
    {
        public static WorldManager Instance => throw new NotSupportedException("Native ECS manager creation is outside this model.");
        public World DefaultWorld => throw new NotSupportedException();

        internal static void ResetForNewSession() => EcsBoundary.Calls.Add("world");
    }

    public sealed class EntityManager
    {
        internal static void ResetForNewSession() => EcsBoundary.Calls.Add("entity");
    }

    public sealed class ReferencePool
    {
        public static ReferencePool Instance => throw new NotSupportedException("Native ECS pool creation is outside this model.");

        internal static void ResetForNewSession() => EcsBoundary.Calls.Add("pool");
    }

    internal static class EcsBoundary
    {
        internal static readonly List<string> Calls = new List<string>();
    }
}
