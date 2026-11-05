using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    public interface IStartupModule
    {
        Task InitializeAsync(CancellationToken cancellationToken);
        void Tick(float deltaTime, float unscaledDeltaTime);
        void Shutdown();
    }

}
