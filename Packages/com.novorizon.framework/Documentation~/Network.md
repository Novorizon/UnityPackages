# 网络：连接、请求与通知

入口为 `Game.Framework.Network.NetworkManager.Instance`。Framework 提供 TCP v2 分帧、Protobuf 请求响应、通知、可配置加密、超时、重连和 Unity 主线程派发。账号、玩家标识、业务消息与业务错误码由项目提供。

## 安装与连接

按 [安装指南](Package.md) 安装主包。Protobuf、BouncyCastle 和 Unsafe 运行时 DLL 已附带；项目不需要 protoc 或 .NET SDK。生成的游戏消息不在主包 Runtime 中。

1. Tools / Novorizon / Create Default Network Settings 创建 Resources/NetworkSettings。
2. 设置 Host、Port、Encryption、CommunicationKeyBase64，核对两端的分帧和加密配置。
3. 在 Unity 主线程使用：

```csharp
using Game.Framework.Network;

var network = NetworkManager.Instance;
network.Initialize(); // 加载配置和安装派发，不联网；重复调用安全。
await network.ConnectAsync(ct);
```

默认不执行任何账号登录。IsConnected 表示传输和可选认证回调已完成；Framework 不提供 PlayerId。主包不会在导入、初始化或重连时读写存档、绑定账号或执行其他游戏业务。

默认地址为 127.0.0.1:1236，AES 密钥需要项目配置。手机上的 127.0.0.1 指手机自身。Android Internet Access 设为 Require。选择 Encryption=None 不会改变项目自己的认证要求。

## 项目认证

在首次 Initialize、订阅或其他会初始化会话的操作之前安装项目回调：

```csharp
network.Initialize(AuthenticateAsync);
await network.ConnectAsync(ct);
```

AuthenticateAsync 的类型是 Func<NetworkManager.AuthenticationContext, CancellationToken, Task>。通过 context.RequestAsync<Response>(projectMessageId, request, token) 发项目登录请求。回调每次重连重新执行；正常请求在认证结束之前被拒绝。回调可能在 I/O 线程运行，不直接访问 Unity 对象。

项目可在需要终止自动重试时抛出 ConnectionRejectedException。RemoteRequestException.ResultCode 保留服务器返回的整数，Framework 不解释任何项目错误码。已初始化会话不能再替换认证回调。

自定义连接配置可在首次 Instance 访问前调用 NetworkManager.Configure(options, clientOptions, dispatcher, authenticate)。Configure 不能替换已创建的实例。只有应用启动层负责配置，页面共享连接。

## 请求与通知

项目自己定义消息枚举和 Protobuf 类型，放在自己的程序集。下面的 ProjectMessageId、ReadRequest、ReadResponse、Notice 都是项目类型：

```csharp
var response = await network.RequestAsync<ReadResponse>(ProjectMessageId.Read, new ReadRequest(), ct);
var subscription = network.Subscribe<Notice>(ProjectMessageId.Notice, OnNotice);

// 页面结束时释放自己的订阅。
subscription.Dispose();
```

消息号可以是 int 或项目自己的 enum。泛型响应类型须实现 Google.Protobuf.IMessage<T> 并具有 public 无参构造函数；库自动缓存 Parser，也保留显式 Parser 重载。

RequestAsync 等待匹配的响应；SendAsync 只等待字节写入。请求按连接代次、请求号和消息号匹配，支持并发和乱序响应。通知号从 2100000000 开始、RequestId 为 0；每个订阅返回独立 IDisposable，退订会抑制待派发回调。

Unity 自动安装主线程派发，无需手动 Update。Connected、Reconnected、Disconnected、Error 以及通知回到主线程。需要手动宿主时可显式注入 INetworkDispatcher 或选择手动模式；自定义 dispatcher 不在 I/O 循环内同步执行游戏回调。

## 心跳、重连与资源边界

NetworkSettings 可配置 HeartbeatIntervalMilliseconds 和 HeartbeatMessageId。默认心跳为 15 秒、消息号 1、空请求与空响应；项目服务器不使用这个心跳约定时将间隔设为 0，或配置自己的消息号。

默认首次连接失败直接返回错误；成功后的断线可自动重连。退避默认为 0.5～30 秒并带抖动。库不自动重放游戏请求；取消、超时或断线也不代表服务器未执行已发送的操作。重要写入由项目设计幂等号和结果查询。

| 边界 | 默认值 |
| --- | --- |
| 连接、认证、请求、单包组装超时 | 各 10 秒 |
| 实际写入超时 | 3 秒 |
| 单包线上大小 | 256 KiB，包含 28 字节头和 AES 模式的 16 字节 tag |
| 待发队列 | 128 包 / 4 MiB，按实际租用容量计 |
| 待响应请求 | 128 |
| 主线程派发队列 | 256 项 / 4 MiB，每批最多 64 个回调 |

TCP v2 使用 28 字节 little-endian 头，包含魔数、版本、消息号、请求号、结果码和消息体长度。粘包、拆包和长度检查由库处理。AES-256-GCM 两端需要相同的 32 字节通信密钥；应用账号凭据与通信密钥分别管理。

页面关闭时取消自己的等待并释放订阅。应用主动断开使用 await network.DisconnectAsync()；整体停机可 await network.DisposeAsync()。Dispose 后旧引用不能重用；退出期间不能重建单例。不要用 Wait/Result 阻塞 Unity 主线程。

## 验证

连接状态、取消、超时、重连和释放由统一仓库 tools/FrameworkServicesVerification/Network.Tests 回归。协议消息、认证、玩家数据和存档 API 放在业务工程，不随 Framework 发布。

实际 Unity 编译与平台网络行为见仓库[验证范围](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.25/Documentation/Validation.md)。
