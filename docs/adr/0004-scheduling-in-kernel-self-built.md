# ADR 0004 — 定时任务进内核:自研零依赖调度器、单触发器模型、DB 选主

- 状态:已采纳(2026-07-26)
- 相关:[[ADR-0001]] / [[ADR-0002]] / [[ADR-0003]];面向读者的文档见 `site/zh/guide/scheduled-jobs.md`

## 背景

定时任务此前有两种互相冲突的方案:做成可选包 `SmartAdmin.Scheduling`,或进内核(`sys_job` 内核 CodeFirst 表、ErrorCode 47xxx 内核段、复用 `FileGcService` 骨架),归属一直没定。同时多处已把定时任务当"现成载体"指望(异步导出中心、日志清理)。运行时依赖红线不变:内核只允许 SqlSugarCore + Microsoft.\*。逐题裁决如下,四项互为因果,一起存档。

## 决策一:进内核,不做可选包

Core 放契约(`IAdminJob`/`JobExecutionContext`/`IJobHandlerResolver`/`CronExpression`/`AdminJobsOptions`/47xxx),Services 放引擎与实体,AspNetCore 放端点;菜单种子进 `DefaultMenuSeed`。理由:①「好接入」= 3 行 Program.cs 开箱即得,可选包做不到;②内核表/错误码段/菜单种子/双前端页面横竖都要进主仓,「可选包」名不副实;③已有多处内核功能把它当现成载体。整模块下线走既有 `Api:DisabledModules`,单副本不想调度走 `Jobs:SchedulerEnabled=false`——不需要"不装"这个自由度。后果:`rebuild-design.md` 的可选包条目与「v1.0 不做任务调度」措辞作废(已同步订正)。

## 决策二:自研零依赖,不吃 Quartz/Hangfire

进内核 ⇒ 依赖红线挡死第三方;反过来若为吃 Quartz 而做可选包,决策一整个塌回去——「留在内核才需要自写,做成可选包就该直接吃 Quartz」的推理链条按决策一取自研支。范围自限使自研面积可控:单机 + 主备(不做分片)、6 段 cron、一任务一触发。以成熟开源 cron 库的实测行为作**行为参照**而非依赖;调度循环采用 deadline 睡眠思路。偏离旧稿:cron 从「5 段 ~100 行」升级为 6 段全套(`* , - / ? L W #`),用户裁定功能强大优先,月末(L)与秒级是真刚需。

## 决策三:一任务 = 一触发(xxl-job 形),不做 Quartz 式的 job/trigger 分表

`sys_job` 单表,每行 = 任务定义 + 触发配置 + 运行状态。1:N 触发器是通用调度框架的需求;管理后台「一个任务一张时刻表」覆盖实况。收益:省一张表、一层嵌套 UI、一组按触发器粒度的状态/接管/补偿语义;同一逻辑要两套时刻表 = 建两行(编译类任务只是多一行配置)。

## 决策四:DB 心跳选主 + 触发 CAS,弃缓存时间桶租约

旧稿设想复用 `FileGcService` 的 `ICacheProvider.IncrementAsync` 时间桶租约——该租约自己的文档写明「时钟差到跨桶,两边可能都领到」,只适合幂等任务,且默认内存缓存下多副本互不相见。改为两层:`sys_job_lock` 单行租约选主(**效率**:谁扫表;心跳 10s / 租约 30s,参数化 `UPDATE ... WHERE` 按影响行数判定,四库通吃)+ 每次触发对 `NextRunTime` 的原子 CAS(**正确性**:`UPDATE sys_job SET NextRunTime=@next WHERE Id=@id AND NextRunTime=@expected`,脑裂/GC 停顿/时钟漂移下同一时刻至多一发)。租约烂掉也不会双发,fencing 不靠 Term。

## 约定与后果

- **存活语义三层**:DB 持久化重启恢复(错过默认 Skip 不补,可选 FireOnceNow 补一次)、双副本主备互备、可选独立 Worker(`AddSmartAdminWorker` 官方配方,`samples/WorkerHost`);消费者默认零新建项目。
- **时间纪律**:全模块服务器本地时间、写库前整秒截断(MySQL datetime 毫秒四舍五入会让 CAS 无声失效)、参与调度的副本必须同 TZ;SQLite 不支持集群形态。
- **载荷三类**:编译 `IAdminJob`(TryAddEnumerable,Scoped)+ HTTP(SSRF 围栏:禁重定向、ConnectCallback 复检解析后 IP、默认只封元数据段)+ SQL(**默认关**,开启即承认任务编辑权 = DBA 权)。属性包(`PropsJson`)是载荷参数的唯一入口。
- **可替换性**:`IJobService`/`IJobLogService`/`IJobHandlerResolver`/`JobSchedulerService` 全部 TryAdd + `virtual`,受 `ReplaceabilityTests` 锁定;可订阅类型不新增必填构造参数。
- 端点表、种子取号、测试与部署纪律以代码和 `site/zh/guide/scheduled-jobs.md` 为准。
