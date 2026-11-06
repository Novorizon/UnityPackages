# 单例静态字段的编辑器重置

Framework 自带基于 Roslyn 3.8 的 Source Generator，最低支持 Unity 2022.3。直接安装 Framework 即可；生成器 DLL 已包含，无须用户安装 .NET SDK、添加 Attribute 或逐类编写 Reset。DLL 标记为 RoslynAnalyzer，禁用普通插件平台，不参与 Player 运行时加载。

```csharp
using System.Collections.Generic;
using Game.Framework;

public partial class ExampleManager : Singleton<ExampleManager>
{
    public static int Count;
    private static int Limit = 100;
    private static List<int> Ids = new List<int> { 1, 2 };
}
```

生成器识别 Singleton<T>、ConfigurableSingleton<T>、MonoSingleton<T> 的直接和间接派生类。拥有静态状态的类型加 partial；嵌套类型的外层类型也需要 partial。没有自己的静态状态的派生类无需修改。每个已使用的闭合泛型类型单独登记，登记表跨 Play 保留；未使用类型无需提前初始化。

## 执行条件

Unity 2022.3：Edit → Project Settings → Editor → Enter Play Mode Settings。启用 Enter Play Mode Options 并取消 Reload Domain 时，UnityFrameworkBootstrap 在 SubsystemRegistration 中先释放旧实例、重置基类引用，然后执行生成的静态重置。开启 Reload Domain 时交给 CLR，不重复执行初始化表达式。进入 Play 时读取当前选项，切换选项无需重编译。

生成的重置只在 UNITY_EDITOR 下存在。Player 没有 EditorStaticReset 类型、生成的重置方法或静态登记字段；运行时 Dispose/Release/OnExit 继续负责资源释放。额外代码不用于登录、切场景、配置重载或整个游戏重启。

Reload Scene 与此独立。关闭 Reload Scene 是保留场景对象、跳过场景重建的选择；框架不会遍历组件、将实例配置恢复为默认值。保留下来的组件使用 OnExit 清理运行资源，OnStart 再次启动；音量、动画速度等已有实例配置保留。

## 恢复规则和限制

- 可赋值静态字段、自动属性：先恢复 default，再按声明执行显式初始化表达式。声明初值改动后重新编译，重置代码同步更新；支持 private、数组和字段式事件。
- readonly List/Dictionary/HashSet：支持 new() 或无参数构造及集合初始化器；保留集合引用，清空并重建初始元素。readonly ConditionalWeakTable 支持清空。
- readonly 不可变值保持原值；object 同步锁、SemaphoreSlim、ReaderWriterLockSlim 保留，避免替换仍有旧异步任务使用的锁。任意 readonly 可变对象不猜测处理，报告 NVR003。
- 不会重新执行任意静态构造函数。只进行 SingletonLifecycle.Register 登记的静态构造可保留；其余有静态状态的类型报告 NVR002，应把状态初值写在声明，把启动动作写在 Initialize/OnStart。
- 不处理普通工具类、嵌套辅助类自己的静态字段、第三方预编译 DLL、ThreadStatic 或自定义事件访问器。ThreadStatic 和自定义事件访问器在受管范围内报告错误。
- 初始化表达式应可重复执行，不依赖其他 Manager 的可变静态状态。直接跨单例读取报告 NVR005；间接方法调用的副作用不可能由语法分析完全判定。重置不等同于完整 Domain Reload。
- 初始化表达式只定义值，不替代资源释放；静态字段持有需释放的资源时，所属模块仍应在退出清理中释放它。

生成代码使用各原始文件的 using 作用域，partial 文件的别名可以不同。先清空所有登记类型，再恢复声明值；单个类型内部按文件路径和声明位置排序，不提供跨 Manager 的初始化依赖编排。恢复失败会汇总抛出，不能静默继续使用残留状态。

## 维护生成器

源码位于 SourceGenerators~/StaticResetGenerator，Unity 不将它当作运行时代码导入。维护者安装 global.json 固定的 .NET SDK 后，使用 `python3 tools/build-static-reset.py` 构建并更新包内 DLL；CI 用 --check 检查源码和发布 DLL 一致，并验证 Editor/Player 两种程序集边界。用户无需执行此命令。

当前自动验证覆盖 Roslyn 3.8 的生成、编译和执行，以及现有程序集模型；尚未在真实 Unity Editor 中执行导入和连续 Play 验证。
