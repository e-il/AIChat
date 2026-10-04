# 项目审查与前端改版

日期：2026-10-05。基于当前工作树的静态审查、后端单元测试及前端浏览器回归，不代表完整安全审计。保留原有未提交改动；后续按要求修复镜像、密钥落盘和记忆关闭问题，未操作真实数据或执行生产部署。

## 已修复的审查问题

### 已修复：生产反向代理镜像引用无效

位置：[docker-compose.prod.yml](docker-compose.prod.yml#L3)。

原引用 `traefik:traefik:experimental-master` 无效，现改为 `traefik:v3.6`。Compose 配置校验通过，远端 manifest 查询确认镜像存在。尚未执行容器部署；该版本标签并非不可变 digest。

### 已修复：修改模型目录会将环境变量密钥写入配置文件

位置：[JsonConfigurationService.cs](backend/AIChat.Api/Services/JsonConfigurationService.cs)。

配置服务现在只负责读取、修改并原子写回模型目录，不缓存运行时 Azure、用户、记忆或提示词设置。独立 `ModelCatalogSettings` 不含 Endpoint/ApiKey；运行时使用 .NET Options，凭据仅来自环境变量，移除 JSON 凭据来源。

合成密钥测试覆盖添加/删除模型不泄漏凭据、目录即时刷新、取消写入不改变文件、无环境变量时不回退到 JSON。原先“先改共享内存再写盘”的状态分叉也随独立快照删除而消除。文件替换后若配置重新加载失败，仍可能出现请求报错但文件已提交的情况，不宣称跨文件事务保证。既有 Azure 客户端缓存的部署切换不在本轮验证范围内。

### 已修复：关闭记忆后仍可能继续提取

位置：[ChatHub.cs](backend/AIChat.Api/Hubs/ChatHub.cs)、[IdleExtractionScheduler.cs](backend/AIChat.Api/Services/IdleExtractionScheduler.cs)。

关闭记忆同时停止检索与收集，取消待处理快照、队列任务及执行中任务的令牌，并阻止已取消任务提交记忆。排除边界按用户及会话持久化；重新开启后不收集关闭期间的消息及其回答。找不到排除边界时停止提取，不猜测历史位置。UI 在服务端确认后才切换状态，失败时保留原状态并提示错误。

两份 Compose 配置增加提取边界与待处理快照持久化卷。升级前需要迁移旧容器的对应文件，挂载新卷不会自动导入旧内容。关闭开关不删除已有记忆，也不能撤回已经发送给模型提供方的数据。

## 待处理问题

### P1：同一浏览器下不同身份共用会话与草稿配置

位置：[conversationStore.ts](frontend/src/services/conversationStore.ts#L3)、[promptProfiles.ts](frontend/src/services/promptProfiles.ts#L8)。

IndexedDB 使用固定数据库名，记录没有用户归属；自定义提示词同样使用全局 localStorage 键。重新鉴权为另一个身份后，会加载同一浏览器原来的会话与自定义提示词。服务端记忆的 userId 隔离不能保护这些本地内容。

建议由服务端提供稳定的非敏感用户标识，按用户划分本地存储，并设计旧记录迁移和身份切换清理策略。不要将访问码直接用作数据库名，也不要无提示地删除旧记录。本次未执行数据迁移。

### P2：声明了文件热加载，但身份权限是启动时快照

位置：[Program.cs](backend/AIChat.Api/Program.cs#L12)、[UserIdentityService.cs](backend/AIChat.Api/Services/UserIdentityService.cs#L10)。

JSON 配置设置了 `reloadOnChange: true`，但身份服务基于启动时 Options 构建映射。运行中删除访问码或撤销管理员权限不会更新当前映射。模型目录已支持更新，但运维不能依赖编辑用户文件立即撤权。

README 已明确用户配置修改后需要重启。若需要立即撤权，仍需实现经过校验的原子快照热更新，并定义现有 SignalR 连接的撤权策略。

### P2：加载侧栏会扫描所有完整消息

位置：[conversationStore.ts](frontend/src/services/conversationStore.ts#L68)。

为了计算会话消息数，列表加载会读取所有消息正文、附件和记忆字段。历史量增大时会产生与全部聊天内容规模相关的 I/O 和内存成本。

建议将消息数保存在会话摘要中，在新增/删除消息的同一事务内更新；迁移时一次性回填。不要为获取几个侧栏计数重复加载完整聊天内容。

## 需要明确的设计约定

- 构建报告 `Microsoft.OpenApi 2.0.0` 的 NU1903 高危依赖告警：[GHSA-v5pm-xwqc-g5wc](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc)。本轮未升级不相关依赖，需要单独评估和修复。
- [AuthCodeMiddleware.cs](backend/AIChat.Api/Middleware/AuthCodeMiddleware.cs#L30) 有意公开媒体 GET。随机文件名是难猜测链接，不是用户授权；任何拿到链接的人都能访问。是否需要签名 URL 或鉴权读取，取决于上传内容的隐私要求。
- [useConversations.ts](frontend/src/hooks/useConversations.ts) 的加载请求没有过期响应保护，持久化失败主要记录到控制台。后续应补充快速切换会话的竞态测试、存储配额失败提示及草稿恢复机制。
- 前端仍发送完整历史，且在断线时清空未完成回答。当前会显示错误并恢复输入，但不支持服务端会话续传或部分回答恢复。

## 本次已处理

- 全面调整侧栏、空白页、输入区、消息、登录、记忆、提示词及模型管理视图，统一为石墨、纸白和朱红工作台。
- 增加历史搜索、可操作的示例草稿、删除确认；移除没有实现的语音按钮和没有依据的套餐展示。
- 移除重复的输入区下拉菜单，复用共享 Dropdown；所有管理面板复用原生 dialog，提供模态焦点隔离和 Escape 关闭。
- 修复取消上传后附件重新出现的问题，补充对象 URL 清理与上传错误反馈。取消会丢弃上传结果，但当前不会中止服务端已经执行的上传。
- 记忆新增或修改失败时保留编辑内容。模型管理增加类别保留、编辑、忙碌状态和失败提示。
- 修复流式发送失败和连接关闭后输入一直锁定的问题，并将输出和错误绑定到发起会话。
- 代码高亮按语言异步加载，修复 Markdown 代码块的不合法 pre 嵌套；移除第二套图标字体和废弃玻璃样式。
- 将浏览器技能迁移至 [.agents/skills/browser-verification/SKILL.md](.agents/skills/browser-verification/SKILL.md)，精简通用入口并按需加载参考资料；删除旧 Claude 专用副本，初始化 [AGENTS.md](AGENTS.md)。

## 验证与边界

- 生产构建与 ESLint 通过，编辑器未报告前端错误。
- 主入口 JS 从约 1,059 KB 降至约 496 KB，gzip 从约 353 KB 降至约 147 KB。语言语法数据仍存在于按需分块中，这不是所有资源总体积的减少量。
- Playwright 使用系统 Edge 通过登录、示例输入、IME、上传取消、记忆编辑及开关失败恢复、模型编辑、提示词保存、历史搜索、流式会话隔离及断线恢复测试。
- 检查 1440、768、390、320 像素宽度下的聊天布局，及平板/手机记忆和提示词面板；截图位于 frontend/artifacts/ui，已人工查看关键截图。
- 测试使用隔离浏览器和模拟 HTTP/WebSocket，不使用真实访问码，不调用 Azure，不写真实记忆。
- 后端 xUnit/VSTest 共 9 项通过：4 项配置测试、5 项记忆模式测试。记忆测试覆盖用户隔离、取消、服务重建后排除历史、缺失边界、旧任务不能提交或释放新任务租约，以及控制器使用认证身份。
- 两份 Compose 配置校验通过；技能 YAML 与相对引用检查通过。
- 初次检查时本地后端 `/api/models` 返回 HTTP 500。真实 HTTP 鉴权、Azure 生成、媒体服务和 Docker 部署未验证；控制器单元测试不替代这些集成检查。

运行方式见 [frontend/TESTING.md](frontend/TESTING.md)。