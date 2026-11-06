# Framework Pool

此示例复用同一发行标签的 Framework 对象池。GpuAnimator 运行时也依赖 Framework 的调度器单例。先安装包依赖，再导入本 Sample。业务预先加载 prefab、网格、材质、纹理与解析后的 AnimationData，然后创建 GpuAnimatorPool。

```csharp
using GpuAnimatorSamples;
using UnityEngine;
var pool = new GpuAnimatorPool(loadedPrefab, sharedData);
var player = pool.Spawn(clipName, Vector3.zero, Quaternion.identity);
pool.Recycle(player);
pool.Dispose();
```

通用对象池直接使用 Framework.GameObjectPool；适配器只管理注入、播放和 Clear。回收先清除播放回调与数据引用，Dispose 同样清理活跃实例。保留加载资源直到池释放，资源卸载由加载方负责。操作从已初始化的 Unity 主线程发起。不要直接销毁或跨池回收实例。
