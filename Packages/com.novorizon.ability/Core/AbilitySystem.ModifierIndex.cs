using System;
using System.Collections.Generic;
using System.Reflection;

namespace Game.Ability
{
    public sealed partial class AbilitySystem
    {
        private readonly Dictionary<int, List<Modifier>> modifiersByParent = new Dictionary<int, List<Modifier>>();
        private readonly HashSet<Modifier> registeredModifiers = new HashSet<Modifier>();
        private readonly HashSet<Projectile> registeredProjectiles = new HashSet<Projectile>();
        private readonly HashSet<Thinker> registeredThinkers = new HashSet<Thinker>();
        private readonly Dictionary<ModifierEventType, List<Modifier>> configuredListeners = new Dictionary<ModifierEventType, List<Modifier>>();
        private readonly Dictionary<ModifierEventType, Dictionary<int, List<Modifier>>> relatedListeners = new Dictionary<ModifierEventType, Dictionary<int, List<Modifier>>>();
        private readonly List<Modifier> customListeners = new List<Modifier>();
        private readonly Dictionary<Modifier, ListenerRegistration> listenerOrder = new Dictionary<Modifier, ListenerRegistration>();
        private static readonly Dictionary<Type, bool> customEventOverrides = new Dictionary<Type, bool>();
        private long nextListenerOrder;

        internal bool IsModifierRegistered(Modifier modifier) => modifier != null && registeredModifiers.Contains(modifier);
        internal bool ContainsProjectile(Projectile projectile) => projectile != null && registeredProjectiles.Contains(projectile);

        private void IndexModifier(Modifier modifier)
        {
            registeredModifiers.Add(modifier);
            int id = modifier.Parent.EntityId;
            if (!modifiersByParent.TryGetValue(id, out List<Modifier> bucket)) modifiersByParent.Add(id, bucket = new List<Modifier>());
            bucket.Add(modifier);

            // Built-in stat/lifecycle modifiers have no event callback. Custom scripts retain
            // the global event contract; configured scripts subscribe to their declared event.
            if (modifier.Script is DefaultModifierScript) return;
            ListenerRegistration registration = new ListenerRegistration { Order = nextListenerOrder++ };
            if (modifier.Script is ConfiguredModifierScript)
            {
                ModifierEventType type = modifier.Definition.TriggerEventType;
                if (type == ModifierEventType.None || modifier.Definition.TriggerActions.Count == 0) return;
                registration.Event = type;
                registration.Global = modifier.Definition.TriggerEventScope == ModifierEventScope.Global;
                registration.ParentId = modifier.Parent.EntityId;
                registration.CasterId = modifier.Caster != null ? modifier.Caster.EntityId : registration.ParentId;
                registration.IndexCaster = IsAbilityLifecycleEvent(type) && registration.ParentId != registration.CasterId;
                if (registration.Global)
                {
                    if (!configuredListeners.TryGetValue(type, out List<Modifier> listeners)) configuredListeners.Add(type, listeners = new List<Modifier>());
                    listeners.Add(modifier);
                }
                else
                {
                    IndexRelated(type, registration.ParentId, modifier);
                    if (registration.IndexCaster) IndexRelated(type, registration.CasterId, modifier);
                }
            }
            else
            {
                // A script that inherits the empty base callback cannot observe events.
                // Actual overrides (including inherited overrides) keep global delivery.
                if (!OverridesModifierEvent(modifier.Script.GetType())) return;
                registration.Custom = true;
                customListeners.Add(modifier);
            }
            listenerOrder.Add(modifier, registration);
        }

        private void UnindexModifier(Modifier modifier)
        {
            registeredModifiers.Remove(modifier);
            if (modifiersByParent.TryGetValue(modifier.Parent.EntityId, out List<Modifier> bucket))
            {
                bucket.Remove(modifier);
                if (bucket.Count == 0) modifiersByParent.Remove(modifier.Parent.EntityId);
            }
            if (!listenerOrder.TryGetValue(modifier, out ListenerRegistration registration)) return;
            listenerOrder.Remove(modifier);
            if (registration.Custom) customListeners.Remove(modifier);
            else if (registration.Global)
            {
                if (configuredListeners.TryGetValue(registration.Event, out List<Modifier> listeners))
                {
                    listeners.Remove(modifier);
                    if (listeners.Count == 0) configuredListeners.Remove(registration.Event);
                }
            }
            else
            {
                UnindexRelated(registration.Event, registration.ParentId, modifier);
                if (registration.IndexCaster) UnindexRelated(registration.Event, registration.CasterId, modifier);
            }
        }

        private void ClearRuntimeIndexes()
        {
            modifiersByParent.Clear();
            registeredModifiers.Clear();
            registeredProjectiles.Clear();
            registeredThinkers.Clear();
            configuredListeners.Clear();
            relatedListeners.Clear();
            customListeners.Clear();
            listenerOrder.Clear();
            nextListenerOrder = 0;
        }

        private List<Modifier> RentUnitModifiers(IUnit unit)
        {
            modifiersByParent.TryGetValue(unit.EntityId, out List<Modifier> bucket);
            return RuntimeSnapshotPool<Modifier>.Rent(bucket);
        }

        private List<Modifier> RentModifierEventListeners(ModifierEvent value)
        {
            var snapshot = RuntimeSnapshotPool<Modifier>.Rent();
            configuredListeners.TryGetValue(value.EventType, out List<Modifier> configured);
            GetRelatedEventLists(value, out List<Modifier> first, out List<Modifier> second);
            int configuredIndex = 0, customIndex = 0, firstIndex = 0, secondIndex = 0;
            // At most four ordered streams: global configured, custom, and related
            // source/target buckets. No full event-bucket scan or per-dispatch sort.
            while (configuredIndex < (configured?.Count ?? 0) || customIndex < customListeners.Count ||
                firstIndex < (first?.Count ?? 0) || secondIndex < (second?.Count ?? 0))
            {
                Modifier next = null;
                int stream = -1;
                ChooseListener(configured, configuredIndex, 0, ref next, ref stream);
                ChooseListener(customListeners, customIndex, 1, ref next, ref stream);
                ChooseListener(first, firstIndex, 2, ref next, ref stream);
                ChooseListener(second, secondIndex, 3, ref next, ref stream);
                if (stream == 0) configuredIndex++;
                else if (stream == 1) customIndex++;
                else if (stream == 2) firstIndex++;
                else if (stream == 3) secondIndex++;
                else break;
                if (stream == 1 || ((ConfiguredModifierScript)next.Script).ShouldHandleEvent(value)) snapshot.Add(next);
            }
            return snapshot;
        }

        private void ChooseListener(List<Modifier> values, int index, int candidateStream, ref Modifier next, ref int stream)
        {
            if (values == null || index >= values.Count) return;
            Modifier candidate = values[index];
            if (next == null || listenerOrder[candidate].Order < listenerOrder[next].Order) { next = candidate; stream = candidateStream; }
        }

        private void IndexRelated(ModifierEventType type, int id, Modifier modifier)
        {
            if (!relatedListeners.TryGetValue(type, out Dictionary<int, List<Modifier>> byUnit)) relatedListeners.Add(type, byUnit = new Dictionary<int, List<Modifier>>());
            if (!byUnit.TryGetValue(id, out List<Modifier> listeners)) byUnit.Add(id, listeners = new List<Modifier>());
            listeners.Add(modifier);
        }

        private void UnindexRelated(ModifierEventType type, int id, Modifier modifier)
        {
            if (!relatedListeners.TryGetValue(type, out Dictionary<int, List<Modifier>> byUnit) || !byUnit.TryGetValue(id, out List<Modifier> listeners)) return;
            listeners.Remove(modifier);
            if (listeners.Count == 0) byUnit.Remove(id);
            if (byUnit.Count == 0) relatedListeners.Remove(type);
        }

        private void GetRelatedEventLists(ModifierEvent value, out List<Modifier> first, out List<Modifier> second)
        {
            first = second = null;
            if (!relatedListeners.TryGetValue(value.EventType, out Dictionary<int, List<Modifier>> byUnit)) return;
            IUnit primary, secondary = null;
            switch (value.EventType)
            {
                case ModifierEventType.DamageTaken:
                case ModifierEventType.Healed:
                case ModifierEventType.Death:
                case ModifierEventType.Respawn: primary = value.Target; break;
                case ModifierEventType.DamageCalculated:
                case ModifierEventType.ProjectileHit: primary = value.Source; secondary = value.Target; break;
                case ModifierEventType.ModifierAdded:
                case ModifierEventType.ModifierRemoved: primary = value.Modifier?.Parent; break;
                default: primary = value.Source; break;
            }
            if (primary != null) byUnit.TryGetValue(primary.EntityId, out first);
            if (secondary != null && (primary == null || secondary.EntityId != primary.EntityId)) byUnit.TryGetValue(secondary.EntityId, out second);
        }

        private static bool IsAbilityLifecycleEvent(ModifierEventType type) => type == ModifierEventType.AbilityExecuted || type == ModifierEventType.AbilityFullyCast || type == ModifierEventType.ChannelFinished;

        private static bool OverridesModifierEvent(Type type)
        {
            lock (customEventOverrides)
            {
                if (customEventOverrides.TryGetValue(type, out bool result)) return result;
                try
                {
                    MethodInfo callback = type.GetMethod(nameof(ModifierScript.OnEvent), BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(ModifierEvent) }, null);
                    result = callback == null || callback.DeclaringType != typeof(ModifierScript);
                }
                // Reflection is only an optimization for the inherited no-op. Legal
                // overloads or restricted metadata must retain global delivery.
                catch (AmbiguousMatchException) { result = true; }
                catch (NotSupportedException) { result = true; }
                customEventOverrides.Add(type, result);
                return result;
            }
        }

        private struct ListenerRegistration
        {
            public long Order;
            public ModifierEventType Event;
            public int ParentId, CasterId;
            public bool Custom, Global, IndexCaster;
        }
    }

}
