# Novorizon Diagnostics

`Tools/Novorizon/Diagnostics` 打开只读 Editor 窗口，支持过滤、暂停、手动采样和 JSON 快照导出。窗口打开时每 0.5 秒采样，关闭后解除 update 订阅；Player 不编入这个包的代码。匹配结果按每页最多 100 条记录展示，用 Previous / Next 翻页并显示页数和总匹配数量。采样、搜索、比较模式或基线改变时才重建匹配缓存，普通重绘和翻页只绘制当前页；大量快照的数据采样、过滤与差异计算仍与记录数有关。

读取已加载包的公开 API，通过 TryGetInstance 取得现有服务，不调用 Instance，不启动或重建已释放服务。未安装、未加载、未创建或计数读取失败会显示状态，不中断其他计数。

覆盖主线程待派发、定时器、网络状态/待响应/待发、场景数量、活动/缓存 UI、页面/弹窗数量、阻塞层、音源和音源池、资源包就绪状态和版本、GPU 播放器数量及当前语言。GameObjectPool 汇总当前登记池数与活动/缓存实例数；ReferencePool 显示已经创建的共享池类型数，不访问 Shared 创建新池。

模块由诊断侧通过公开接口适配；Framework、UI 和资源包不反向依赖诊断包。不扫描私有字段，不提供强制清理按钮。

回归覆盖未创建服务不被构造、嵌套计数及异常隔离；窗口代码另外核对真实 Unity Editor DLL 的编译签名。实际窗口交互需在持有许可证的 Editor 验收。

## 快照差异

用 Set baseline 保存当前快照，Compare baseline 查看指标新增、变化和移除，Clear baseline 清除比较。Export JSON 同时导出当前快照、基线和差异。
