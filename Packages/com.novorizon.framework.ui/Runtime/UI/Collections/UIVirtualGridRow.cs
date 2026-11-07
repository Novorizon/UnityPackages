#nullable disable
using System;
using UnityEngine;

namespace UI
{
    /// <summary>Internal ownership for cells belonging to one pooled virtual row.</summary>
    public sealed class UIVirtualGridRow : MonoBehaviour
    {
        internal UIVirtualGrid Owner;
        internal GameObject[] Cells = Array.Empty<GameObject>();
        internal int[] Indices = Array.Empty<int>();
        internal Action<GameObject> Unbind;

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

        internal void ReleaseAll()
        {
            for (int column = 0; column < Indices.Length; column++)
            {
                Release(column);
            }
        }

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
