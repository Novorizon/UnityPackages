using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Ability
{
    /// <summary>技能运行时对应的快照。保存采样时刻的数据，供其他流程读取。</summary>
    [Serializable]
    public sealed class AbilityRuntimeSnapshot
    {
        public readonly List<AbilityUnitRuntimeSnapshot> Units = new List<AbilityUnitRuntimeSnapshot>();
        public readonly List<ModifierRuntimeSnapshot> Modifiers = new List<ModifierRuntimeSnapshot>();
        public readonly List<ProjectileRuntimeSnapshot> Projectiles = new List<ProjectileRuntimeSnapshot>();
        public readonly List<ThinkerRuntimeSnapshot> Thinkers = new List<ThinkerRuntimeSnapshot>();
        public readonly List<PresentationHandleInfo> PresentationHandles = new List<PresentationHandleInfo>();
    }

    /// <summary>技能单位运行时对应的快照。保存采样时刻的数据，供其他流程读取。</summary>
    [Serializable]
    public sealed class AbilityUnitRuntimeSnapshot
    {
        public int EntityId;
        public int TeamId;
        public Vector3 Position;
        public bool IsAlive;
        public readonly List<AbilityInstanceRuntimeSnapshot> Abilities = new List<AbilityInstanceRuntimeSnapshot>();
    }

    /// <summary>技能实例运行时对应的快照。保存采样时刻的数据，供其他流程读取。</summary>
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

    /// <summary>状态修饰器运行时对应的快照。保存采样时刻的数据，供其他流程读取。</summary>
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

    /// <summary>状态修饰器属性运行时对应的快照。保存采样时刻的数据，供其他流程读取。</summary>
    [Serializable]
    public sealed class ModifierPropertyRuntimeSnapshot
    {
        public ModifierProperty Property;
        public float Value;
    }

    /// <summary>投射物运行时对应的快照。保存采样时刻的数据，供其他流程读取。</summary>
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

    /// <summary>持续效果对象运行时对应的快照。保存采样时刻的数据，供其他流程读取。</summary>
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
