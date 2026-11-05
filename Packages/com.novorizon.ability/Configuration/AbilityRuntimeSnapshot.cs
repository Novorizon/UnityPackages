using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Ability
{
    [Serializable]
    public sealed class AbilityRuntimeSnapshot
    {
        public readonly List<AbilityUnitRuntimeSnapshot> Units = new List<AbilityUnitRuntimeSnapshot>();
        public readonly List<ModifierRuntimeSnapshot> Modifiers = new List<ModifierRuntimeSnapshot>();
        public readonly List<ProjectileRuntimeSnapshot> Projectiles = new List<ProjectileRuntimeSnapshot>();
        public readonly List<ThinkerRuntimeSnapshot> Thinkers = new List<ThinkerRuntimeSnapshot>();
        public readonly List<PresentationHandleInfo> PresentationHandles = new List<PresentationHandleInfo>();
    }

    [Serializable]
    public sealed class AbilityUnitRuntimeSnapshot
    {
        public int EntityId;
        public int TeamId;
        public Vector3 Position;
        public bool IsAlive;
        public readonly List<AbilityInstanceRuntimeSnapshot> Abilities = new List<AbilityInstanceRuntimeSnapshot>();
    }

    [Serializable]
    public sealed class AbilityInstanceRuntimeSnapshot
    {
        public string Name;
        public int Level;
        public AbilityPhase Phase;
        public bool Activated;
        public bool ToggleEnabled;
        public float CooldownRemaining;
        public int Charges;
    }

    [Serializable]
    public sealed class ModifierRuntimeSnapshot
    {
        public string Name;
        public int ParentEntityId;
        public int CasterEntityId;
        public string AbilityName;
        public int Stacks;
        public float Duration;
        public float RemainingTime;
        public UnitState States;
        public readonly List<ModifierPropertyRuntimeSnapshot> Properties = new List<ModifierPropertyRuntimeSnapshot>();
    }

    [Serializable]
    public sealed class ModifierPropertyRuntimeSnapshot
    {
        public ModifierProperty Property;
        public float Value;
    }

    [Serializable]
    public sealed class ProjectileRuntimeSnapshot
    {
        public string Name;
        public string AbilityName;
        public int CasterEntityId;
        public int TargetEntityId;
        public Vector3 Position;
        public bool Tracking;
        public bool Destroyed;
    }

    [Serializable]
    public sealed class ThinkerRuntimeSnapshot
    {
        public string AbilityName;
        public int CasterEntityId;
        public Vector3 Position;
        public float Duration;
        public float Interval;
        public float Radius;
        public bool Destroyed;
    }
}
