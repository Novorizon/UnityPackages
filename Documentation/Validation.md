# 验证范围

导入使用前一轮校验后的 UPM 归档，共 11 个库包，加独立聚合安装包。`ImportBaseline.json` 记录 2642 个导入文件的 SHA-256；检查要求保留全部可执行源码、程序集、资源和 meta，允许自有包 Markdown 中的安装地址调整。官方 HybridCLR、YooAsset 全部文件要求与导入基线相同。

工作区检查覆盖：12 个包的名称、版本、自有依赖闭包与环路、Framework 依赖方向、ECS/Ability 独立性、聚合归档哈希、同仓库版本标签、主动导入的程序集名称和 GUID 唯一性，以及两个 Unity 工程的本地依赖路径。

迁入的可执行验证包括：

- 聚合安装器：原 20 组模型场景，加同仓库相邻目录安装场景；使用 Unity/UPM API 模型验证请求恢复、失败、重试、批量依赖与源码安装路径。
- GameStarter：21 组生命周期、真实 Framework MonoSingleton 集成及可选模块安装模型场景。
- RTree：保留原正确性与空间查询回归程序，直接编译统一仓库的 canonical Runtime 源码；覆盖树变更、查询及空间索引一致性。

使用 .NET SDK 8 运行 `python3 tools/workspace.py verify`。这些检查不执行真实 Unity API、UPM 服务或渲染。历史仓库中其他验证结果属于原提交的记录，本次不据此声称重新运行了全部旧仓库测试。

## 尚未执行的 Unity 验收

当前环境没有 Unity Editor、IL2CPP 平台模块、GPU 或设备。

1. Unity 2022.3.62f1 分别打开 GameStarter 和 PackageValidation，检查本地文件依赖、asmdef、Native 插件及资源导入。
2. GameStarter 默认仅加载 Framework，重复 Play/Stop，并分别关闭 domain reload / scene reload 检查重启。
3. 从本地 package.json 和分发 tarball 安装聚合包，检查所有包版本、目录复制和跨重编译恢复；复制完整工作区到新路径后再次打开。
4. 验证官方 HybridCLR Embedded 来源、Data~、原 importer meta、默认配置和热更入口；执行 IL2CPP Player 构建与补丁加载。
5. 按各包原生文档验收 UI 输入和资源、Ability Odin 编辑器、ECS 生命周期、空间查询、GPU 烘焙/播放/回收与 YooAsset Bundle 加载。
6. 在 Unity 中单独验证同仓库标签下的 Git 子目录安装；GitHub 提交、标签及源码一致性由发布流程单独核对。
