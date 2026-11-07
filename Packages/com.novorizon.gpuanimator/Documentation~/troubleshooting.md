# 问题排查

## 菜单未出现或编译报错

先确认所有自有包使用同一发行标签，且没有重复导入旧程序集。`main` 的运行时不引用 PureMVCFramework；若仍报相关类型缺失，检查项目内是否残留旧源码。使用 asmdef 的业务程序集需要引用 `GpuAnimator`，Editor 工具由独立的 `GpuAnimator.Editor` 程序集编译。

包声明的最低 Unity 版本是 2022.3。附带 Shader 使用 Built-in Render Pipeline 和 `#pragma target 3.5`；目标图形 API 与 GPU 需要相应的整数运算和实例化能力。URP 工程安装 com.novorizon.gpuanimator.urp 并选择匹配的 URPUnlit/URPLit 烘焙选项；HDRP 仍需自行适配 Shader。不能只更换播放脚本解决管线不匹配。


## 烘焙数量为零或没有输出

在 **GpuSkinning > Baker** 核对源目录；工具扫描该目录的直接角色子目录，不使用 Project 窗口当前选择。No PBR/PBR 快捷菜单仍扫描默认的 `Assets/Artworks/GpuRole/`。

每个角色需要恰好一个 FBX、`res/<Role>.png` 和至少一个独立 `.anim`。FBX 文件名和 SMR 节点名不限，不再要求输入 `.mat`。FBX 内嵌动画不会由此接口自动收集。检查仅大小写不同的重复候选，并按[美术资源规范](art-guidelines.md#交付目录)整理资源。

先执行 **Validate all roles** 并查看窗口或 Console 的逐角色报告。它完整采样、检查矩阵和容量，并预检输出路径、资源类型、只读文件及其 `.meta`、PNG 和 Shader 属性，但不会保存输出；成功验证后仍需执行 **Bake all roles**。一个角色失败不会阻止批次处理其余角色。没有单独的报告导出文件。

若按钮不可用，先退出 Play 模式并等待导入、编译结束；再检查源目录是否存在，源与输出是否都在本项目 `Assets/` 内、且不相同也不互相包含。目录字段不接受 `.` 或 `..` 路径段。

## 多个部件验证失败，或材质结果与原模型不同

生成器要求整个模型层级中恰好一个 SMR，包括未激活节点；网格需要单个三角形 submesh。多个 SMR 或多 submesh 会验证失败，不会默默取最后一个。需要先在制作流程中合并或分别交付，不会自动合并身体、头发和装备。

工具配置生成 Shader、主贴图、可选遮罩、动画纹理、骨骼数和动画纹理尺寸。它不复制输入材质；重新烘焙会保留已有输出材质上工具未管理的属性。使用 PBR 时仍需按项目配置 LUT 等设置。URPLit 生成 prefab 开启投射和接收阴影，并补齐缺失切线；其他模式关闭。旧 prefab 手动换为 Lit 材质时还需自行开启 Renderer 阴影选项并确认切线，灯光和 URP Asset 也需启用阴影。

## 重新烘焙、失败或取消后有哪些资源会改变

新工具不会删除整个角色输出目录。它在内存中准备完整数据，写入前备份本次涉及的文件；该角色保存失败会回滚本次写入，成功保存则保留已有生成资源的 GUID。输出目录中的其他文件不作为清理对象。升级后若 Mesh 文件名仍是旧名称，是因为现有 prefab 引用了输出目录内独立的 Mesh `.asset`，工具沿用其路径以保持引用。

如果保存失败后连回滚也发生错误，工具会报告并保留 `Library/GpuAnimatorBakeBackup/` 中的备份位置。先根据错误检查磁盘权限和文件状态，保留这份备份用于恢复，不要继续覆盖该角色。

批次取消不会撤销已成功保存的角色，也不会继续处理后面的角色。根据角色结果检查完成范围；修复失败项后可重新烘焙。生成 prefab、Mesh、动画纹理和元数据仍由工具更新，自定义行为建议放在包装 prefab 或 Variant 中。

## Initialize 或 TryPlay 返回 false

| 结果 | 检查内容 |
| --- | --- |
| `Initialize` 返回 `false` | Renderer 是否存在；元数据及 `clips` 是否非空；`totalFrame` 是否大于零。失败后旧绑定已被 Clear |
| `TryPlay` 返回 `false` | 是否已初始化；名称是否与 `clip.name` 精确一致；帧区间是否在 `[0, totalFrame - 1]`；帧率是否有限且大于零 |
| `FromBytes` 抛异常 | 是否加载了对应的 `_Data.bytes`，而不是 PNG、普通文本或其他对象；数据是否来自兼容的烘焙版本 |

Clip 名称来自 `AnimationClip.name`，不一定等于文件名。遍历 `sharedData.clips` 检查实际名称。空项会被跳过；重复名称会匹配第一项。`TryPlay` 失败不会停止旧动画，`Play` 则没有返回结果，排查时优先用 TryPlay。

## 初始化成功但不动，或动作不对

初始化不会自动播放，请显式调用 TryPlay。生成 prefab 虽然已经带 GpuAnimator，却仍需要调用方解码并注入元数据。

必须成套使用同次烘焙产生的 Mesh、材质、动画 PNG 与 `.bytes`。即使 Initialize 返回 true，它也没有验证资源是否配对。Renderer 上应使用 GPU 动画 Shader；材质的 `_AnimationTex`、`_BoneNum`、`_AnimationTexSize` 要对应生成数据。不要把主贴图 `<Role>.png` 当成动画纹理 `<Role>_Data.png`。

检查组件是否启用、对象是否激活、`AutomaticUpdate` 是否为 true；手动模式需要外部持续调用 Tick。若每帧重新调用 TryPlay，计时会不断清零，动画会停留在开头。播放循环动作时让业务逻辑只在状态改变时切换。

## 播放过快、复用后停住或起点不一致

同时启用自动 Update 和外部 Tick 会重复推进。统一管理时将每个实例的 `AutomaticUpdate` 设为 false；恢复自动模式时显式设回 true。Clear/Initialize 不会重置这个属性。

`TryPlay` 默认从第一帧开始；便捷方法 `Play` 默认随机起始相位。需要确定起点时使用 `TryPlay(name)` 或显式 `randomizeStart: false`。随机不是分配独立动画纹理，每个实例仍共享相同资源。

## 完成回调没有触发或切换时旧回调消失

只有非循环动画自然结束才会调用一次完成回调。`loop: null` 使用烘焙设置，强制一次性播放可传 `loop: false`。从第一帧开始，长度为 N 的动画在逻辑上经过 N 个采样间隔后完成；正常逐采样推进时包含末帧的间隔，较大的 Tick 则可能跳过采样帧，直接跨越完成边界。

跨越完成边界时，末帧提交和回调执行发生在同一次 Tick 中。回调若立即 TryPlay，可能在屏幕渲染前覆盖末帧，所以回调触发不保证末帧已经实际显示，更不保证显示了一个完整间隔。

Stop、Clear、重新 Initialize 或成功启动另一段动画都会取消旧的待执行回调，不会把取消视作自然完成。`TryPlay` 失败则保留原播放及回调。组件停用只会停止自动 Update；项目自己的异步任务和外部 Tick 仍应由适配层管理。

## 自定义实例颜色被下一帧覆盖

播放器缓存并回写整份 PropertyBlock。如果其他代码修改 Renderer 的 PropertyBlock，请在修改后调用 `player.SetPropertyBlock()`。同时保留块中的其他属性，让 `_FrameIndex` 与 `_BlendFrameIndex` 由播放器控制，详见 [PropertyBlock 协作](integration.md#propertyblock-协作)。

## 动画在镜头边缘突然消失

先用 3.1 工具重新烘焙：生成器会针对骨骼影响的顶点范围，合并所有采样动作的保守 bounds 并增加余量；仅升级包不会更新旧 Mesh 的静态 bounds。

MeshRenderer 在 CPU 端使用 Mesh bounds 剔除，不会重新读取 Shader 的变形结果。若业务修改了动画资源、额外改变 Shader 顶点位置或手动覆盖生成 Mesh 的 bounds，需要同步更新剔除范围。只调用 `RecalculateBounds()` 仍只会看到 CPU 中未蒙皮的顶点。共享 Mesh 的 bounds 修改会影响所有使用它的实例。

## 缩放、扭转或过渡与期望不同

格式保存每骨骼一个带符号的统一缩放值；最终蒙皮矩阵需要能在数值容差内由位移、旋转和这一缩放值表示。非均匀缩放、剪切会验证失败，不能靠重新导入纹理修复。检查父级、绑定矩阵和动画组合，不要只看某根骨骼的局部缩放。蒙皮采用最多四权重 LBS，关节大角度扭转时不提供 DQS 的体积保持效果。

当前 Shader 不执行帧间插值和 Clip 过渡混合；`_BlendFrameIndex` / `_BlendProgress` 属性的存在不表示已经启用交叉淡化。需要更平滑的时间采样时，可以提高输入 Clip 的烘焙帧率；需要真正的插值/混合则需扩展实现。

从缩放修复前的旧版本升级时重新烘焙动画 PNG。旧版对缩放向量取 magnitude 并截断到 `[0,1]`，已经丢失的数据不能仅靠新解码器修复；数据类型改名本身不要求重烘焙。新烘焙器在隔离预览场景中为每个 Clip 创建新实例，并在每帧恢复基准 TRS；缺失轨道不会继续继承前一个 Clip 的采样姿态。

## 非循环动作的时长与源 Clip 不完全相同

新烘焙器为非循环 Clip 保存从零开始的采样，并补入精确结束时刻；循环 Clip 不重复保存时长边界。现有播放器每个输出帧使用一个完整采样间隔，因此非循环动作的名义完成时间为 `输出帧数 / frameRate`，包含末帧停留间隔。不要仅按源 `clip.length` 安排动作回调；大 Tick 和回调切换还会影响实际可见的末帧，见上面的完成回调说明。

## 根位移、BlendShape 或材质动画未按原控制器运行

此流程烘焙的是骨骼变换，不是整个 Animator Controller。根节点或根骨骼的位移可以反映在视觉姿态中，但播放器不会用它移动 GameObject。BlendShape、材质、激活状态等非 Transform/Animator 数值曲线，以及对象引用曲线会验证失败；Transform 曲线路径也必须存在。即使没有 BlendShape 动画曲线，Renderer 的默认 BlendShape 权重也必须有限且为零；非零默认形变会被拒绝，未使用的零权重形变可以保留。动画事件、IK 和业务挂点没有对应的运行时执行流程。需要 GameObject 位移或事件时，由接入层负责；需要形变目标时，应扩展格式或使用其他制作方案。

含 Animator/肌肉曲线的 Clip 会出现验证提醒，Humanoid 类型本身不作为自动拒绝条件。当前自动化检查没有验证重定向和 IK 的 Unity 实际结果，需在项目中逐个播放确认。

## 动画纹理导入后数据损坏

生成器把数值编码进 RGBA 字节，因此动画 PNG 应保持：

- Point 过滤、关闭 mipmap。
- 关闭 sRGB，使用无压缩 RGBA32，保留 alpha。
- 保留生成尺寸，不进行缩放或有损平台压缩。

新烘焙器根据窗口上限检查容量，并设置动画 PNG 的 RGBA32、Point、无 mipmap、关闭 sRGB 和不缩放等导入选项，同时处理已有的已知平台覆盖；保存后还会检查实际导入尺寸。超出容量或导入尺寸不符会让该角色失败，避免留下被缩小的动画数据纹理。

上限默认 `2048`，可在 Baker 修改。它是资源预算，不保证目标 GPU 支持设置的最大值。切换 Build Target、重新设置平台覆盖、使用后处理器或外部压缩工具后，仍应检查实际导入尺寸是否与 `_AnimationTexSize`、元数据 `texWidth/texHeight` 一致。不要把数值动画 PNG 当成普通颜色贴图压缩，也不要将关闭 sRGB 的规则套到角色主贴图。

默认的当前烘焙路径每骨骼每帧使用 8 个 RGBA32 像素。增加骨骼数、Clip 数或烘焙帧率会增加纹理大小；运行时的 Tick 跳帧处理不会降低烘焙资源的存储量。
