namespace Game.Ability
{
    /// <summary>Area/runtime object for persistent ground logic, similar to Dota thinkers.</summary>
    public sealed class Thinker
    {
        private const float TimeEpsilon = 0.000001f;
        public AbilitySystem Engine { get; }
        public Ability Ability { get; }
        public IUnit Caster { get; }
        public UnityEngine.Vector3 Position { get; }
        public float Duration { get; }
        public float Interval { get; }
        public float Radius { get; }
        public ThinkerScript Script { get; }
        public bool IsDestroyed { get; private set; }

        private float remainingTime;
        private float intervalTimer;
        private bool isRegistered;

        internal Thinker(AbilitySystem engine, ThinkerRequest request)
        {
            Engine = engine;
            Ability = request.Ability;
            Caster = request.Caster;
            Position = request.Position;
            Duration = request.Duration;
            Interval = request.Interval;
            Radius = request.Radius;
            remainingTime = request.Duration;
            intervalTimer = request.Interval;
            Script = request.Script ?? new DefaultThinkerScript();
            Script.Bind(this);
        }

        internal void OnRegistered()
        {
            if (isRegistered || IsDestroyed) return;
            isRegistered = true;
            Script.OnCreated();
        }

        internal void Tick(float deltaTime)
        {
            using (Engine.BeginExecution())
            {
                TickActiveTime(deltaTime);
            }
        }

        private void TickActiveTime(float deltaTime)
        {
            if (IsDestroyed || !Engine.IsRuntimeAvailable || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;

            float activeTime = Duration > 0f ? UnityEngine.Mathf.Min(deltaTime, UnityEngine.Mathf.Max(0f, remainingTime)) : deltaTime;
            if (Duration > 0f) remainingTime -= activeTime;
            if (!Engine.TryConsumeExecutionBudget("Thinker update"))
            {
                if (Duration > 0f && IsDue(remainingTime, Duration)) Engine.RemoveThinker(this);
                return;
            }
            Script.OnThink(activeTime);
            if (IsDestroyed || !Engine.IsRuntimeAvailable) return;

            if (Interval > 0f)
            {
                intervalTimer -= activeTime;
                int callbacks = 0;
                while (IsDue(intervalTimer, Interval))
                {
                    if (callbacks >= UnityEngine.Mathf.Max(1, Engine.MaxPeriodicCallbacksPerUpdate) || !Engine.TryConsumeExecutionBudget("Thinker interval"))
                    {
                        float remainder = -intervalTimer % Interval;
                        intervalTimer = remainder == 0f ? Interval : Interval - remainder;
                        break;
                    }

                    intervalTimer += Interval;
                    callbacks++;
                    Script.OnIntervalThink();
                    if (IsDestroyed || !Engine.IsRuntimeAvailable) return;
                }
            }

            if (Duration > 0f && IsDue(remainingTime, Duration)) Engine.RemoveThinker(this);
        }

        private static bool IsDue(float timer, float period)
        {
            return timer <= UnityEngine.Mathf.Min(TimeEpsilon, period * 0.00001f);
        }

        internal void Destroy()
        {
            if (IsDestroyed) return;
            IsDestroyed = true;
            Script.OnDestroy();
        }
    }
}
