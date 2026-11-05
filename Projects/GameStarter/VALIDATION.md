# 验证范围

## 本轮已执行

- GameStarter 及其编辑器代码编译：.NET SDK 8，Release，警告视为错误，0 警告、0 错误。
- 实际 Framework Core 源码单独编译为 `Game.Framework.Core`；实际 `MonoSingleton.cs` 编译为另一个 `Game.Framework.Unity` 程序集；没有复制一个用于运行时的单例替代实现。
- 21 组生命周期/安装流程验证：空启动；异步顺序；逆序释放；部分初始化失败；取消期间的迟到返回；重入；重复模块；更新失败；清理异常；正常帧零列表复制分配；实际单例重复与 Play Mode 重启模型；依赖拓扑与错误目录；归档校验；UPM 请求丢失后的恢复；中途失败后重试；版本核验；等待官方 HybridCLR 的原生 UPM Embed 请求完成后才继续安装；编译/播放时暂停；依赖就绪后才执行配置。
- 结构核验：每个 Assets 资源有稳定且唯一的 GUID，Bootstrap 场景绑定 GameEntry，Build Settings 指向 Bootstrap，运行时程序集只引用 Framework，所有 Git 源固定到 40 位提交 SHA。
- 分发包核验：九个 UPM 归档 SHA-256 和 package.json 身份/版本核验，包含官方 HybridCLR 9.0.3；不包含 Odin。导出从固定排序与时间戳生成，不包含 Library、构建缓存或 Git 数据。

测试中的 UnityEngine/UnityEditor/UPM 是 API 模型。它们能捕获生命周期和安装状态错误，不能证明 Unity 的真实资源导入、序列化、domain reload 时序和 UPM 本地归档路径解析完全正确。

## Unity 中的验收

环境尚未提供 Unity Editor、平台构建模块、GPU 或设备，以下没有执行：

1. 用 2022.3.62f1 打开源码版和附带归档版；确认 UPM 解析、所有 asmdef 与 Bootstrap 场景正常导入。
2. Bootstrap 场景 Play/Stop 两次；再关闭 domain reload/scene reload 重复操作，确认每轮只启动一次，Console 出现 `GAME_STARTER_READY`。
3. 添加两个项目 StartupModule，检查顺序、逆序清理及异步取消。
4. 使用窗口分别安装 UI、Ability、RTree、GpuAnimator、ECS，确认 Package Manager 版本和 Samples；Ability Odin 编辑器分别检查缺少 Odin 与导入有授权 Odin 后的行为。
5. 安装 HybridCLR，确认官方包嵌入、Data~ 和导入器 meta 保留，默认 Resources 设置和热更入口生成，UPM 触发脚本重编译后安装队列能继续。
6. 使用封装 Build New Player 构建一份 IL2CPP Player，再用 Export Update 生成补丁，在实际 Player 上验收；编译失败时不得将旧输出误当作新补丁发布。
7. 验证网络中断、Editor 中途退出、已有其他版本的嵌入包和项目编译错误时的提示与重新安装流程。

工程不自带示例游戏画面；空场景没有摄像机是预期。GpuAnimator 本轮支持范围仍为 Built-in，实际烘焙/渲染、UI 点击/资源加载和原生 HybridCLR 构建需要在各自包的真实 Unity 验收中检查。
