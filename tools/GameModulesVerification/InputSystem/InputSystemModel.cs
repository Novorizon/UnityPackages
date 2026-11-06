// Deterministic API boundary model; no native input processing, device detection or event timing is asserted here.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;

namespace UnityEngine.InputSystem.Utilities
{
    public readonly struct ReadOnlyArray<T> : IReadOnlyList<T>
    {
        private readonly T[] values;
        public ReadOnlyArray(T[] values) => this.values = values;
        public int Count => values?.Length ?? 0;

        public T this[int index] => values[index];
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(values ?? Array.Empty<T>())).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace UnityEngine.InputSystem
{
    public class InputDevice
    {
    }

    public struct InputBinding
    {
        public bool isComposite;
        public string groups;
        public static InputBinding MaskByGroup(string group) => new InputBinding
        {
            groups = group
        };
    }

    public struct InputControlScheme
    {
        public string name, bindingGroup;
    }

    public class InputActionAsset : ScriptableObject, ICloneable
    {
        public ReadOnlyArray<InputActionMap> actionMaps;
        public ReadOnlyArray<InputControlScheme> controlSchemes;
        public InputBinding? bindingMask;
        public ReadOnlyArray<InputDevice>? devices;
        public string OverridesForTest = "{}";
        public InputActionMap FindActionMap(string name, bool throwIfNotFound = false)
        {
            return actionMaps.FirstOrDefault(map => map.name == name) ?? throw new ArgumentException(name);
        }

        public InputAction FindAction(string name, bool throwIfNotFound = false)
        {
            return actionMaps.SelectMany(map => map.ActionsForTest).FirstOrDefault(action => action.name == name || action.actionMap.name + "/" + action.name == name) ?? throw new ArgumentException(name);
        }

        public int FindControlSchemeIndex(string name)
        {
            for (int i = 0; i < controlSchemes.Count; i++)
            {
                if (controlSchemes[i].name == name)
                {
                    return i;
                }
            }

            return -1;
        }

        public void Disable()
        {
            foreach (var map in actionMaps)
            {
                map.Disable();
            }
        }

        public string SaveBindingOverridesAsJson() => OverridesForTest;
        public void LoadBindingOverridesFromJson(string json) => OverridesForTest = json;
        public object Clone()
        {
            var clone = CreateInstance<InputActionAsset>();
            clone.actionMaps = new ReadOnlyArray<InputActionMap>(actionMaps.Select(map => map.CloneForTest()).ToArray());
            clone.controlSchemes = controlSchemes;
            clone.OverridesForTest = OverridesForTest;
            return clone;
        }
    }

    public class InputActionMap
    {
        public string name;
        public readonly List<InputAction> ActionsForTest = new List<InputAction>();
        public bool enabled => ActionsForTest.Any(action => action.enabled);

        public InputActionMap CloneForTest()
        {
            var clone = new InputActionMap
            {
                name = name
            };
            foreach (var action in ActionsForTest)
            {
                clone.ActionsForTest.Add(new InputAction { name = action.name, actionMap = clone, bindings = action.bindings });
            }

            return clone;
        }

        public void Enable()
        {
            foreach (var action in ActionsForTest)
            {
                action.enabled = true;
            }
        }

        public void Disable()
        {
            foreach (var action in ActionsForTest)
            {
                action.Disable();
            }
        }
    }

    public class InputAction
    {
        public struct CallbackContext
        {
        }

        public string name;
        public InputActionMap actionMap;
        public ReadOnlyArray<InputBinding> bindings;
        public bool enabled;
        public event Action<CallbackContext> performed, canceled;
        public void PerformForTest()
        {
            if (enabled)
            {
                performed?.Invoke(default);
            }
        }

        public void Disable()
        {
            bool wasEnabled = enabled;
            enabled = false;
            if (wasEnabled)
            {
                canceled?.Invoke(default);
            }
        }
    }

    public static class InputActionRebindingExtensions
    {
        public static bool FailCreationForTest;
        public static RebindingOperation LastForTest;
        public static RebindingOperation PerformInteractiveRebinding(this InputAction action, int bindingIndex)
        {
            if (FailCreationForTest)
            {
                throw new InvalidOperationException("Invalid rebinding configuration.");
            }

            return LastForTest = new RebindingOperation();
        }

        public sealed class RebindingOperation : IDisposable
        {
            public bool DisposedForTest;
            private bool finished;
            private Action<RebindingOperation> complete, cancel;
            public RebindingOperation WithCancelingThrough(string value) => this;
            public RebindingOperation WithControlsExcluding(string value) => this;
            public RebindingOperation OnComplete(Action<RebindingOperation> callback)
            {
                complete = callback;
                return this;
            }

            public RebindingOperation OnCancel(Action<RebindingOperation> callback)
            {
                cancel = callback;
                return this;
            }

            public RebindingOperation Start() => this;
            public void CompleteForTest()
            {
                finished = true;
                complete?.Invoke(this);
            }

            public void Cancel()
            {
                if (!finished)
                {
                    finished = true;
                    cancel?.Invoke(this);
                }
            }

            public void Dispose() => DisposedForTest = true;
        }
    }
}
