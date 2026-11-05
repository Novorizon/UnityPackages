#nullable enable
using Game.Framework.Pooling;
using UnityEngine;

namespace Game.Framework.Samples.Demo
{
    public sealed class DemoReferenceContext : IReferencePoolable
    {
        public GameObject? Target;
        public int Amount;
        public void Reset() { Target = null; Amount = 0; }
    }
}
