#nullable disable
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static partial class Time
    {
        public static float deltaTime = .016f;
    }

    public class Renderer : MonoBehaviour
    {
        private MaterialPropertyBlock values = new MaterialPropertyBlock();
        public void GetPropertyBlock(MaterialPropertyBlock block)
        {
            block.Copy(values);
        }

        public void SetPropertyBlock(MaterialPropertyBlock block)
        {
            values.Copy(block);
        }
    }

    public class MaterialPropertyBlock
    {
        readonly Dictionary<int, int> values = new Dictionary<int, int>();
        public void SetInt(int id, int value)
        {
            values[id] = value;
        }

        public int GetInt(int id) => values.TryGetValue(id, out int value) ? value : 0;
        public void Copy(MaterialPropertyBlock source)
        {
            values.Clear();
            foreach (var p in source.values)
            {
                values[p.Key] = p.Value;
            }
        }
    }

    public static class Shader
    {
        public static int PropertyToID(string name) => name.GetHashCode();
    }

    public static class Random
    {
        public static int Range(int min, int max) => min;
    }
}

namespace UnityEngine.Scripting.APIUpdating
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class MovedFromAttribute : System.Attribute
    {
        public MovedFromAttribute(bool autoUpdateAPI, string sourceNamespace, string sourceAssembly, string sourceClassName)
        {
        }
    }
}