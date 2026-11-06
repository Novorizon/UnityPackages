# 验证范围

## 仓库检查

`python3 tools/workspace.py check` 检查依赖闭包、包来源、程序集/GUID、导入基线与已记录的修改/退役文件。`python3 tools/check-docs.py` 检查维护文档的本地链接和声明的示例路径。

## .NET 回归

`python3 tools/workspace.py verify --dotnet /path/to/dotnet` 编译实际包源码，运行以下检查。第三方依赖只进入临时缓存，不放入 package。

| 范围 | 实际覆盖 | 边界 |
| --- | --- | --- |
| 单例与生成器 | 生命周期、退出、线程宿主代际隔离、静态初值恢复、Domain Reload 关闭后的顺序 | Unity 生命周期 API 使用模型 |
| HybridCLR 封装 | 显式启动、确认、取消、超时、程序集边界、签名资源配对与回退 | 不安装原生 il2cpp，不执行解释器 |
| 聚合安装 | 依赖排序、版本核对、UPM 请求恢复、超时后重试、Embed 和默认配置 | UPM API 使用模型 |
| 通用服务 | 网络、UI、GPU 调度、场景、存储、输入、音频、资源租约 | 网络含真实 loopback；引擎 API 使用模型 |
| Luban | 官方 CLI 的 Excel → C# → bytes → Tables、失败保留、并发与取消 | 不执行 Unity 资源导入 |
| RTree | 随机/差分查询、变更与节点不变量 | 不代表目标设备性能 |
| Ability | 公开 API、编辑器 Codec/Workspace/Sandbox、缺少 Odin 的编译 | Unity 编辑器交互使用模型 |

当前结果写入 Verification.json；来源和历史基线用于追溯，不作为本次原生验收结果。

## Unity 原生验收

使用独立 [GameStarter](https://github.com/Novorizon/GameStarter)，按[云端编译与验收](https://github.com/Novorizon/GameStarter/blob/main/Documentation/UnityValidation.zh-CN.md)执行：

1. 从 GitHub 检出工程，以 manifest 中的固定来源安装包。
2. 导入全部 Samples，重新编译 Editor 和 Player 脚本。
3. 运行 Bootstrap、可交互 Demo 与 GPU EditMode 测试。
4. 在支持目标平台的主机上构建 IL2CPP 主包；验证补丁、资源版本配套与新表结构。

通过 .NET 回归不能替代 Unity 导入、Shader/烘焙、原生 Destroy、输入音频或 IL2CPP。原生结果以该环境生成的日志、测试 XML 和报告为准。
