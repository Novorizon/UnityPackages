using System;
using UnityEngine;

namespace Game.Ability
{
    /// <summary>Keep finite float-world geometry finite while comparing distances and normalizing directions.</summary>
    internal static class AbilityGeometry
    {
        internal static bool WithinDistance(Vector3 from, Vector3 to, float distance)
        {
            double x = (double)to.x - from.x;
            double y = (double)to.y - from.y;
            double z = (double)to.z - from.z;
            return x * x + y * y + z * z <= (double)distance * distance;
        }

        internal static Vector3 Normalize(Vector3 direction)
        {
            double x = direction.x, y = direction.y, z = direction.z;
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (length == 0)
            {
                return Vector3.zero;
            }
            return new Vector3((float)(x / length), (float)(y / length), (float)(z / length));
        }
    }
}
