using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game
{
    public abstract class StartupModule : MonoBehaviour, IStartupModule
    {
        public virtual Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual void Tick(float deltaTime, float unscaledDeltaTime) { }
        public virtual void Shutdown() { }
    }
}
