# Unity 原生验收

2026-11-08（北京时间），使用已激活许可证的 Linux Unity **2022.3.62f1**、URP **14.0.12**、TMP **3.0.6**、Mesa **llvmpipe OpenGL 4.5** 实际运行：

| 套件 | 结果 | 覆盖 |
| --- | --- | --- |
| GPU/URP/Story/Diagnostics EditMode | **65/65 通过，无跳过** | 烘焙、纹理编码/导入、源资源保持不变、重烘焙 GUID、保存失败恢复、URP Shader 导入/Pass、Lit 阴影标记与切线；剧情 SO 保存/卸载/重导入；诊断不创建单例/不读私有 getter 及真实 JSON 往返；严格元数据类型、混合骨骼逆转置 GPU 法线回读 |
| NativeAcceptance PlayMode | **38/38 通过，无跳过** | 真实 Canvas/ScrollRect 的动态列表、延迟原生 Destroy、真实 TMP 文本/默认字体与多语言隐藏重开、UI Demo/虚拟行字形网格、安全区投影、实际 GPU 动画位置/法线像素回读；本地化 Settings/Bootstrap；剧情分支/立绘/语音/销毁；VP8 蓝色帧回读、准备超时与迟到资源；完整 Basic Story 示例；Grid/Input 重入；ECS/Ability/Audio/GPU 与热路径分配；真实 YooAsset 空下载器取消时序；账号回调生命周期、AnalyticsManager 的 Inspector 埋点与 AttributionManager 后台回调回主线程 |

这是 v0.1.0-preview.36 基线加 preview.37 全部 22 个本地包覆盖的验收；结果记录于 [NativeAcceptance.json](../../Documentation/NativeAcceptance.json)，包括被测源码和本地 XML/日志摘要。原始记录位于执行环境的 `/workspace/UnityPackagesNativeValidation/ValidationResults`，不把账号/许可证日志提交到仓库。

## 复现

先完成 Unity 安装、账号登录和有效许可证激活。需要 Python 3、Git；无显示器的 Linux 还需 Xvfb/xauth 和可用的 OpenGL 驱动。

```sh
python3 tools/workspace.py unity-project --output /absolute/NativeValidation --include-acceptance-tests
export UNITY_EDITOR_PATH=/path/to/2022.3.62f1/Editor/Unity
tools/NativeAcceptance/run.sh /absolute/NativeValidation
```

脚本先在独立 batch-mode Editor 中导入官方 TMP Essential Resources，并等待导入完成、验证默认字体后再执行测试；不通过直接复制第三方资源到 SDK 来提供字体。使用 `--include-acceptance-tests` 时，生成工程从本目录复制 EditMode、PlayMode 和 GPU 测试，再导入全部 9 个已声明 Samples（包括 UI Demo 和 Basic Story）及自生成的 VP8/WAV 测试素材。

生成工程使用 workspace.json 的固定发行标签，通过 Git/注册表安装正式依赖，不依赖开发机器的 SDK 源码路径。开发修复时可在该独立工程 manifest 显式覆盖待测包为本地路径；必须记录基线和覆盖列表。不要同时开启同一工程的多个 Editor。

可在项目完成首次导入后只运行受影响阶段：

```sh
tools/NativeAcceptance/run.sh /absolute/NativeValidation PlayMode
```

结果在工程 ValidationResults 中。只有本轮生成的非零测试 XML 且 result=Passed 才算通过；Editor 编译、测试失败或没有生成结果都返回失败。脚本保留真实图形上下文，不使用 -nographics。自定义密钥环/登录运行环境由调用方准备，不在 SDK 中存储凭证。

## 本次发现与修复

- Unity 2022 的程序集版本条件不能使用 `[1.0.0,)` / `[0.1.0,)`。改用下限表达式 `1.0.0` / `0.1.0`，使可选 uGUI/Luban 程序集参与实际编译。
- ForceReserializeAssets 不保证清除已更新原生对象的 dirty 状态，导致 GPU 重烘焙被误判为保存失败。改用 SaveAssetIfDirty，限定保存本次生成的 Mesh 和 Material；重烘焙 GUID 与失败恢复用例实际通过。
- Unity 会为位置曲线补齐其他轴，测试改为保存并比对全部绑定/关键帧。五骨骼影响用例使用新 Mesh 布局，避免直接把旧四权重属性改为变长布局时触发 Unity 原生格式错误，并确认实际保留了五个影响。

## 验收边界

GPU 回读用例对 URP Lit 的 DepthNormals Pass 执行真实 DrawMesh，再读取像素验证已知骨骼旋转/平移和帧索引。它不代表完整 Forward 光照、投射/接收阴影画面、所有关键字组合、目标设备性能或批处理已通过。安全区用例使用该 Editor 实际报告的 Screen.safeArea；刘海/灵动岛和 Home Indicator 的具体边界仍需模拟器/真机。

仍需在目标环境执行：

- 方向光级联和点光/聚光阴影、接收阴影、SSAO、Forward+/Deferred、相机深度及完整视觉比对。
- iPhone 横竖屏、小屏/长屏、安全区、触摸、键盘显示/隐藏、切后台恢复、实际字体缺字和长文排版。
- macOS + Xcode 下的 iOS IL2CPP/AOT/裁剪、Metal 和设备性能；其他目标平台的构建/设备回归。

已有 UISafeArea/CanvasScaler；统一软键盘避让尚未提供。TMP 绑定、Inspector Key 搜索/预览、本地语言表及统一 Settings/Bootstrap 均已提供。原生文本用例使用默认字体可覆盖的 en/fr 文案，不把它当作 CJK 字形覆盖证明。

剧情视频用例播放自生成 VP8/WebM，并读取预期蓝色像素；覆盖准备超时、取消、不可跳过、销毁后迟到租约释放和完整示例分支。语音用例只验证实际 AudioClip/AudioSource 的协议与租约，不验证物理声音输出或设备音画同步。诊断归属用模型验证独立租约状态，以真实 Editor 验证不创建服务与 JsonUtility 字段；没有据此测量原生 YooAsset 内存或引用计数。

本轮正确性与性能审查见 [PackageAudit](../../Documentation/PackageAudit.zh-CN.md)。性能输出位于测试 XML 的 output 中；预热零分配只覆盖指明操作，不包括完整帧、TMP 排版和业务回调。
