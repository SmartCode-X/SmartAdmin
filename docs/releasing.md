# 发布流程(Release Runbook)

SmartAdmin 前后端同仓、**同版本号一起发**。后端以 NuGet 包发布(版本在 CI 打包时经 `-p:Version` 注入,取 tag 的号);前端内核以 npm 包 `smart-admin-web` 发布,版本写在 `web/packages/admin/package.json`,workspace 根与 `web/template` 的 `version` 和它同号(模板的号就是模板登录页页脚显示的版本),模板对 `smart-admin-web` 的依赖精确钉在 `X.Y.Z`(前端页面调的端点跟着后端版本走,浮动范围会装到比 NuGet 新的包);文档站顶栏徽章在 `site/.vitepress/config.ts`(中/英各一处)。这几处必须一起 bump,否则 npm 上的包、用户在 UI / 文档站看到的版本和实际跑的 NuGet 包对不上。

版本号规则:**主版本号 = 内核所用的 .NET 主版本**(10.x 对应 .NET 10,下一次大版本跟随下一个 .NET LTS),次版本号加功能,修订号修 bug;全部包共用一个版本号。破坏性变更尽量攒到换 .NET 大版本时一起发;周期内确需破坏的在次版本发,并在 CHANGELOG 对应版本段落顶部加粗提示。

节奏:**开发在 `dev`,发布在 `main`**——先把 `dev` 合进 `main`,再在 **`main`** 上打 `v*` tag。tag 一推,`.github/workflows/release.yml` 接手:校验 tag 落在 `main` 上、CHANGELOG / 前端版本 / 模板依赖版本 / 文档站徽章一致,构建、测试、模板冒烟、打 13 个 nupkg 与前端 tgz,全绿才经 **Trusted Publishing** 推 nuget.org、发 npm,并建 GitHub Release。仓库里不保存任何 NuGet API key 或 npm token。**发出去的版本不可撤回**:nuget.org 只能 unlist,npm 上用过的版本号也不能再用。真正的闸门是 workflow 的 verify 阶段:它红了不会发包。

## 一、准备(可逆,在 `dev` 上做)

1. **定版 CHANGELOG**:保留顶部空的 `## Unreleased`,把待发布条目移到紧随其后的 `## X.Y.Z - YYYY-MM-DD`,按 Keep a Changelog 归类(Added / Fixed / Changed / Removed)。条目按**真实 diff** 写,别照提交标题猜——快速拉自上个 tag 以来的提交:

   ```powershell
   git log vX.Y.<上一个>..dev --oneline
   ```

2. **bump 版本号**(后端不用改文件,pack 时注入;前端三个 package.json + 模板依赖版本 + **文档站导航徽章**一起改):
   - `web/packages/admin/package.json`(npm 包本身)、`web/package.json`(workspace 根)、`web/template/package.json` 的 `version`
   - `web/template/package.json` 的 `dependencies["smart-admin-web"]` 改成 `X.Y.Z`(精确版本,不带 `^`)
   - `web/package-lock.json`:改完上面几处,在 `web/` 下跑 `npm install --package-lock-only` 同步(会改 5 处:顶层 `version`、`packages[""]`、`packages["packages/admin"]`、`packages["template"]` 的 `version` 与模板的依赖版本;diff 里不该出现别的变化)
   - **`site/.vitepress/config.ts`**:英文 nav 与中文(`zh`)nav **各一处**版本徽章 `{ text: 'X.Y.Z', link: 'https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md' }`(文档站顶栏显示的版本号;`release.yml` 的 verify 会数这两处,不是 `X.Y.Z` 直接拦)。agent 流程见 `skills/smart-release.md`(`/smart-release`)。
   - 核对无残留(PowerShell):`Select-String -Path web/package.json,web/packages/admin/package.json,web/template/package.json -Pattern 'X\.Y\.<旧>'` 与 `Select-String -Path site/.vitepress/config.ts -Pattern "'X.Y.<旧>'"` 都应为空;`Select-String -Path site/.vitepress/config.ts -Pattern "text: 'X.Y.Z'"` 应正好 2 处。

3. **本地验绿**(可选但推荐):`ci` workflow 会在 push 上跑同样的检查,本地先过一遍能少等一轮。本机内存紧,**别并发**跑 vue-tsc 和 dotnet test,一次一个:

   ```powershell
   Push-Location web; try { npm run lint; if ($LASTEXITCODE -ne 0) { throw 'Vue lint failed' }; npm run format:check; if ($LASTEXITCODE -ne 0) { throw 'Vue format check failed' }; npm test; if ($LASTEXITCODE -ne 0) { throw 'Vue tests failed' }; npm run build; if ($LASTEXITCODE -ne 0) { throw 'Vue build failed' } } finally { Pop-Location }
   dotnet test backend/SmartAdmin.slnx -c Release; if ($LASTEXITCODE -ne 0) { throw '.NET tests failed' }
   pwsh templates/smoke-test.ps1; if ($LASTEXITCODE -ne 0) { throw 'template smoke failed' }
   ```

   模板冒烟(`smoke-test.ps1`)把内核打进临时本地 feed,再 `dotnet new smart-app` 生成消费方项目并 build、启动,验的是消费方拿到包后的第一条命令。改了 `site/**` 还要跑 `cd site; npm run lint:prose; npx vitepress build`。

4. 提交:`chore(release): 准备 X.Y.Z — changelog 定版 + 前端与文档站版本号`(提交信息规范见 [`skills/write-commit.md`](../skills/write-commit.md):中文主题 + conventional-commit),推 `dev`,等 `ci`(以及改了容器相关文件时的 `docker-smoke`)绿。

## 二、发布(合 `main` + 打 tag;tag 触发发包)

先确认 `main` 能干净快进(理想情况,`dev` 一路领先没分叉):

```powershell
git fetch origin
git merge-base --is-ancestor origin/main origin/dev
if ($LASTEXITCODE -eq 0) { Write-Host '可 ff' } else { Write-Host '有分叉，需真合并' }
```

然后:

```powershell
# 1. 推 dev
git push origin dev

# 2. main 快进到 dev；不重置本地分支
git fetch origin
if (git show-ref --verify --quiet refs/heads/main) { git switch main } else { git switch --track -c main origin/main }
if ((git rev-parse main) -ne (git rev-parse origin/main)) { throw 'Local main differs from origin/main; inspect it before merging.' }
git merge --ff-only origin/dev
git push origin main

# 3. 在当前 origin/main tip 打 tag 并推——推 tag 就是发包的扳机
git fetch origin
$head = git rev-parse HEAD; $main = git rev-parse main; $originMain = git rev-parse origin/main
if (($head -ne $main) -or ($head -ne $originMain)) { throw 'Tag target is not the current origin/main tip.' }
git tag vX.Y.Z
git push origin vX.Y.Z
```

`--ff-only` 失败,说明有人直接改了 `main`(它有 `dev` 没有的提交):先 `git merge dev` 解冲突,确认版本号/CHANGELOG 没被顶回旧值,再推。

推完 tag 去 Actions 页盯 `release` workflow(或 `gh run list --workflow release.yml --limit 1` 拿到 id 后 `gh run watch <id> --exit-status`):

- **verify** 红了不会进 publish,此时删 tag 重打是安全的:

  ```bash
  git tag -d vX.Y.Z
  git push origin :refs/tags/vX.Y.Z
  ```

- **publish** 跑完,包就在 nuget.org 与 npm 上了,只能 unlist / 弃号、再发修订版。publish 中途失败(比如 NuGet 推完、npm 没发出去)直接重跑这个 job:NuGet 带 `--skip-duplicate`,npm 那步先查版本在不在,已发的都会跳过。

`release` 只跑 SQLite 腿 + Redis 契约测试,而且同一 SHA 的 `ci` 已把 `backend (sqlite)` 跑绿时连这一步也跳过(构建一次、晋级发布;带版本号的构建、模板冒烟、打包、抓 openapi 照跑);四库矩阵与 Docker 冒烟由 `ci` / `docker-smoke` 在 push 上跑,发版前确认 `main` 最近一次都是绿的。

## 三、发布后

- **核对 nuget.org**:`SmartAdmin`、`SmartAdmin.Templates` 等的 `X.Y.Z` 可见(索引有几分钟延迟);空目录里 `dotnet new install SmartAdmin.Templates::X.Y.Z` 后 `dotnet new smart-app` 能还原。
- **核对 npm**:`npm view smart-admin-web@X.Y.Z version` 输出 `X.Y.Z`;npmjs.com 包页的版本旁有 provenance 标记(证明是从本仓库 `release.yml` 构建发布的)。空目录里 `npx degit SmartCode-X/SmartAdmin/web/template web; cd web; npm install` 能装上新版。
- **GitHub Release**:workflow 自动建,说明取自 CHANGELOG 对应版本段落,附 13 个 nupkg、前端 tgz 与 `openapi.json`,不用手工建。「发了什么」的真源始终是 tag + CHANGELOG.md。
- **文档站**:`docs` workflow 在 `main` 上构建并自动部署到 GitHub Pages(仓库设置里 Pages 的 Source 选 GitHub Actions)。部署步骤按运行时的仓库可见性门控,仓库若转私有会退回只构建不部署,那时需要把 `site/.vitepress/dist` 发到自己的静态托管。核对站顶导航徽章已是 `X.Y.Z`。
- 发布成功后,前端 `package.json` 与文档站导航徽章已是新版本,`dev` 继续开发,下一版从新的 `## Unreleased` 攒起。

## 四、一次性准备(换账号 / 换仓库时才需要)

- **nuget.org Trusted Publishing 策略**:登录 nuget.org → 用户名 → Trusted Publishing → Create。Package owner `Andy_Zhong`,Repository Owner `SmartCode-X`,Repository `SmartAdmin`,Workflow File `release.yml`(只填文件名,不带路径),Environment 留空,Scopes 允许发布新包,glob `SmartAdmin*`(不带点,否则匹配不到元包 `SmartAdmin` 本身)。注意:**在仓库还是私有时建的策略只临时生效 7 天**,窗口内必须完成一次成功发布才会永久激活;过期了在页面上重新激活即可。仓库已公开时建的策略没有这个窗口。
- **⚠ 删掉 GitHub 仓库重建(哪怕同名)会废掉现有策略,必须删了重建。** 策略成功发过一次包之后,nuget.org 会把 GitHub 的 **repository id 与 owner id 永久锁进策略**——这是防"resurrection attack"的设计:否则谁都能删掉一个仓库、用同名重建,再冒充它发包。重建后的仓库是**新的 id**,策略页面照旧显示 `Active`,但锁的是那个已经不存在的旧 id,发版会在 `NuGet/login` 那步被拒。**没有改绑入口,只能 Delete 再 Create。** 危险之处在于它不会提前报错:等你发现时 tag 已经推出去了,而 tag 不可逆,只能弃号补发下一版。
  - **怎么自查**(30 秒,换仓库后务必做一次):策略卡片上 `Repository: SmartAdmin #<数字>` 里的数字,要和 `gh api repos/<owner>/<repo> --jq .id` 的输出一致;`Repository Owner` 后面的数字对应 `--jq .owner.id`。
- nuget.org 用户名默认写在 `release.yml` 里(`Andy_Zhong`);换账号时改仓库变量 `NUGET_USER`,不用改 workflow。
- **npm Trusted Publisher**:npm 只能给已经存在的包建信任关系,所以包的**第一个版本**由 npm 账号 `smartcode-x` 在本地手动发:

  ```powershell
  npm login                      # smartcode-x,账号要开 2FA
  Push-Location web; npm ci; npm test; Pop-Location
  Push-Location web/packages/admin; npm publish; Pop-Location   # prepack 自动构建;发之前 npm pack --dry-run 可先看文件清单
  ```

  发完到 npmjs.com → `smart-admin-web` → Settings → Trusted Publisher,选 GitHub Actions:Organization or user `SmartCode-X`,Repository `SmartAdmin`,Workflow filename `release.yml`(只填文件名),Environment 留空,**Allowed actions 勾上直接 `npm publish`**(新建的配置默认只允许 `npm stage publish`,不勾的话 `release.yml` 的发布会被拒)。字段区分大小写,npm 保存时不校验,填错要到发版那一步才报错;建好的配置不能改,只能删了重建。再到 Settings → Publishing access 选 "Require two-factor authentication and disallow tokens":之后任何 token 都发不了版,能发的只有 `release.yml`(OIDC)和维护者本人过 2FA 的交互式 `npm publish`。
  - 信任关系还核对 `web/packages/admin/package.json` 的 `repository.url`(`git+https://github.com/SmartCode-X/SmartAdmin.git`,区分大小写),仓库搬家或改名时两边一起改。
  - Trusted Publishing 只支持 GitHub 托管的 runner,npm CLI 要 11.5.1 以上;`release.yml` 的 npm 那步用 Node 24 并在发布前断言 npm 版本。
- 仓库公开,GitHub Actions 不计分钟数,`ci` 的 SqlServer 全量定时腿每晚照跑。若哪天转回私有,每月只有 2000 分钟免费额度,那条定时腿会按可见性自动停。

> 发布节奏与闸门的**为什么**在 [CHANGELOG.md](../CHANGELOG.md) 顶部;本页是**怎么做**的操作清单。
