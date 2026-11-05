# 独立包交付

本轮确认 ECS 2.0.0-preview.2 使用 Framework 0.10.0-preview.1（实测源码提交 65affcd12ece943da81ae7152c4e4682cde71b99）。ECS 单向依赖 Framework，Framework 不增加 ECS 专用规则；ECS 与 Ability 无依赖关系。

通用单例使用 Framework.MonoSingleton；World、EntityManager 与 ECS 动态 ReferencePool 保留 ECS 的初始化、更新和清理契约。ECS 的池按运行时 Type/工厂、组件与系统初始化顺序工作，不能直接用不同契约的 ReferencePool<T> 替换。GameObjectProvider/Lease 继续管理外部资源租约。

2026-11-05 重新运行 Tests~/verify.sh：核心 92、外部消费者 15、示例 10、单例生命周期 14、池 40，共 171 项通过。存储验证包括 3000 次随机变更、100000 次重复注入与两个 Hybrid arity。外部消费检查分离八个程序集，并保留两种 Framework/ECS 重置先后顺序的释放、旧实体退役和重新创建验证。

制作 UPM 包：`python3 Tools~/package.py --output /tmp/com.novorizon.ecsframework-2.0.0-preview.2.tgz`。先在 Package Manager 安装配套 Framework 主包及 Mathematics 1.2.6，再安装 ECS tarball。源码、GUID 和运行时实现保持已验证版本；交付不包含测试编译产物、性能历史或开发工具。

当前验证环境没有 Unity Editor；不能据此声称 Unity 原生生命周期、IL2CPP 或设备已通过。Unity 2022.3 实际 Play/禁用 domain reload 场景仍需目标项目验收。接入边界详见 FRAMEWORK-INTEGRATION.zh-CN.md。
