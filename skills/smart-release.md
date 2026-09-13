# SmartAdmin 发布新版本（smart-release）

**仅用于 SmartAdmin 本仓。** 前后端同仓、**同版本号一起发**。本 skill 是 agent 可执行的发布流程；操作细节与「为什么」见 [`docs/releasing.md`](../docs/releasing.md)（人类 runbook，环境侧真源）。

**节奏**：开发在 `dev`，发布在 `main`。先合 `dev` → `main`，再在 **`main` 上**打 `v*` tag；tag 一推，`release` workflow 校验、测试、打包，经 Trusted Publishing 推 nuget.org（13 个 nupkg）并发 npm（前端包 `smart-admin-web`）。仓库里没有 NuGet API key 或 npm token，agent 不在本地 pack / push / publish。

**本 skill 不代用户拍板版本号、不代推 tag。** 推 tag 就等于发包，每到不可逆步，先陈述现状并请用户确认后再执行。

---

## 触发

用户说「SmartAdmin 发版 / smart-release / 打 tag / 推 nuget / 准备 X.Y.Z」或 `/smart-release` 时用本 skill。不要在普通 feature 提交流程里自动进入，也不要在其他仓库误用。

---

## 前置：先确认 Actions 能调度

**这是进流程前第一件事,先于定版。** `release.yml` 是唯一的发包链路——Trusted Publishing 换发布权限靠 GitHub 签发的 OIDC token,本地 pack / push / `npm publish` 不是退路(仓库和本机都不该有 NuGet API key 或 npm token)。日常验证有本地 `ci.bat` 顶着,发版没有替代方案。

仓库是公开的,Actions 不计分钟数,但 job 调不起来还有别的原因:仓库设置里 Actions 被关、GitHub 侧故障、或仓库转回私有后账单欠费/超限。**这类失败不是执行失败,是调度层直接拒绝**,换 self-hosted runner 也绕不过。查:

```powershell
gh run list --limit 5
```

近期 run 若是 **0–2 秒的 failure**,基本可以断定是调度层拒绝,**不要去查代码**。看它给的原因:

```powershell
gh run view <run-id>
```

比如这一行就是账单问题,去 GitHub **Settings → Billing & plans** 恢复付款方式或调高 spending limit:

```
The job was not started because recent account payments have failed
or your spending limit needs to be increased
```

**另外**:如果 GitHub 仓库**删除重建过**(哪怕同名),先让用户核对 nuget.org 的 Trusted Publishing 策略——它把仓库 id 永久锁死,重建后的新 id 对不上,策略页面却仍显示 `Active`,发版会在 `NuGet/login` 被拒而 tag 已推出去。自查与修法见 [`docs/releasing.md`](../docs/releasing.md) 第四节。仓库没动过就跳过这条。

**npm 侧同样先核对**:`npm view smart-admin-web version` 能查到包,且用户确认 npmjs.com 上 `smart-admin-web` 的 Trusted Publisher 已指向 `SmartCode-X` / `SmartAdmin` / `release.yml`、Allowed actions 含直接 `npm publish`。包还不存在或没配信任关系时,`release.yml` 的 npm 那步必然失败,而此时 NuGet 已经推出去了。首版手动发布与配置步骤见 [`docs/releasing.md`](../docs/releasing.md) 第四节,由用户本人操作,agent 不代发。

**完成标准**:最近一次 workflow 能正常调度(不是秒级 failure),或用户明确知情并坚持继续。**调不起来就不要推 tag**——推 tag 等于发包,而 tag 是不可逆的:workflow 起不来,包发不出去,tag 却已经在远端了,只能补推一个新版本号收拾。

---

## 0. 定版

1. 确认发版源分支：默认 `dev`；若用户指定其他分支，设为 `$source`，下文都使用该值。同步并切到它（PowerShell）：

   ```powershell
   $source = 'dev' # 替换为用户确认的发版源分支
   git fetch origin --tags
   if (git show-ref --verify --quiet "refs/heads/$source") {
     git switch $source
   } else {
     git switch --track -c $source "origin/$source"
   }
   git pull --ff-only origin $source
   ```
2. 读当前：
   - 最新发布 tag：`git tag -l 'v*' --sort=-v:refname | Select-Object -First 5`
   - 前端：`web/packages/admin/package.json` 的 `version`（npm 包本身；`web/package.json` 与 `web/template/package.json` 应与它同号）
   - **文档站导航徽章**：`site/.vitepress/config.ts` 里中/英两处 `{ text: '…', link: '…/CHANGELOG.md' }`（当前应是上一发布版）
   - CHANGELOG：`## Unreleased` 下有无实质条目
3. 与用户确认目标版本 `X.Y.Z` 与发布日 `YYYY-MM-DD`。主版本号跟随内核所用的 .NET 主版本（10.x 对应 .NET 10），次版本号加功能、修订号修 bug，规则见 `CHANGELOG.md` 顶部。

**完成标准**：用户明确说出目标版本号；本地在 `$source` 且工作区干净，或用户知情接受脏树。

---

## 1. 准备（可逆，在 `$source`）

### 1.1 CHANGELOG

保留顶部空的 `## Unreleased`，把待发布条目移到紧随其后的 `## X.Y.Z - YYYY-MM-DD`。条目按 **真实 diff** 写，按 Keep a Changelog 归类（Added / Fixed / Changed / Removed）。别照提交标题猜。

拉自上个 tag 以来的提交：

```powershell
git log "v<上一版>..$source" --oneline
```

**完成标准**：`CHANGELOG.md` 已有 `## X.Y.Z - ` 标题；`## Unreleased` 空段保留在顶部（给下一版攒）；用户看过条目无异议。

### 1.2 bump 版本号（前端 + 文档站）

后端不用改文件版本，pack 时 `-p:Version` 注入。**前端三个 package.json、模板依赖版本、文档站导航徽章必须一起改到 `X.Y.Z`**：`release.yml` 的 verify 逐一核对，任何一处不对都拦发版；漏文档站会在站顶导航显示旧版。

| 位置 | 改什么 |
|------|--------|
| `web/packages/admin/package.json` | `version`（发到 npm 的就是这个号） |
| `web/package.json` | `version`（workspace 根） |
| `web/template/package.json` | `version`（模板登录页页脚显示它）+ `dependencies["smart-admin-web"]` 改成 `X.Y.Z`（精确版本，不带 `^`：前端页面调的端点跟着后端版本走） |
| `web/package-lock.json` | 不手改：上面三处改完，在 `web/` 下跑 `npm install --package-lock-only`；diff 应只有这几个包的 `version` 与模板的依赖版本 |
| **`site/.vitepress/config.ts`** | **英文 nav + 中文（`zh`）nav 各一处**版本徽章 `{ text: 'X.Y.Z', link: 'https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md' }`。搜 `text: 'P.Q.R'` 应正好两处，都改成新版 |

不要改文档正文里第三方包的版本号（如 `smart-naive-table` 的依赖范围），那些不是发布徽章。

核对无残留（旧版 `P.Q.R`）：

```bash
# PowerShell
Select-String -Path web/package.json,web/packages/admin/package.json,web/template/package.json -Pattern "P\.Q\.R"
git diff --stat -- web/package-lock.json
Select-String -Path site/.vitepress/config.ts -Pattern "P\.Q\.R"
# 文档站徽章必须已是新版（期望 2 处）
Select-String -Path site/.vitepress/config.ts -Pattern "text: 'X.Y.Z'"
```

**完成标准**：三个前端 version、模板依赖 `X.Y.Z`、两处文档站徽章、CHANGELOG 标题一致；lockfile 已同步；`config.ts` 无旧版 `text: 'P.Q.R'`。

### 1.3 本地验绿

`ci` workflow 会在 push 上跑同样的检查，本地先过一遍能少等一轮；本机内存紧时**串行**，不要并行 vue-tsc 与 `dotnet test`：

```powershell
Push-Location web
try {
  npm run lint
  if ($LASTEXITCODE -ne 0) { throw 'Vue lint failed' }
  npm run format:check
  if ($LASTEXITCODE -ne 0) { throw 'Vue format check failed' }
  npm test
  if ($LASTEXITCODE -ne 0) { throw 'Vue tests failed' }
  npm run build
  if ($LASTEXITCODE -ne 0) { throw 'Vue build failed' }
  npm pack -w smart-admin-web --dry-run   # 看一眼要发到 npm 的文件清单：dist/、README.md、LICENSE、package.json
  if ($LASTEXITCODE -ne 0) { throw 'npm pack failed' }
} finally { Pop-Location }

dotnet test backend/SmartAdmin.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw '.NET tests failed' }

pwsh templates/smoke-test.ps1
if ($LASTEXITCODE -ne 0) { throw 'template smoke failed' }
```

模板冒烟把内核打进临时本地 feed，再 `dotnet new smart-app` 生成消费方项目并 build、启动，验的是消费方拿到包后的第一条命令。改了 `site/**` 还要 `cd site; npm run lint:prose; npx vitepress build`。

**完成标准**：lint + 测试 + build + 模板冒烟全部 exit 0（含 site 时 lint:prose 与 vitepress build 也过）。任一步红则停，修在 `$source`，不进入第二节。

### 1.4 提交准备提交

```text
chore(release): 准备 X.Y.Z — changelog 定版 + 前端与文档站版本号
```

**完成标准**：该提交在 `$source` 上，且 diff 含 `site/.vitepress/config.ts`；`git status` 干净。

---

## 2. 发布（不可逆：合 main + 打 tag，tag 触发发包）

### 2.1 推 dev 与快进检查

```powershell
git fetch origin
git push origin $source
git merge-base --is-ancestor origin/main "origin/$source"
if ($LASTEXITCODE -eq 0) { Write-Host '可 ff' } else { Write-Host '有分叉，需真合并' }
```

有分叉时：真合并 `origin/$source`，确认 CHANGELOG / 版本号没被顶回旧值，再继续。

### 2.2 合 main（需用户确认）

向用户复述将执行：

1. `main` 快进（或合并）到含准备提交的 `$source`
2. `git push origin main`

用户同意后再跑。此处绝不重置或强推本地 `main`；本地 `main` 与远端不一致时停下，查明未推送提交的归属后再继续。PowerShell：

```powershell
git fetch origin
if (git show-ref --verify --quiet refs/heads/main) {
  git switch main
} else {
  git switch --track -c main origin/main
}
if ((git rev-parse main) -ne (git rev-parse origin/main)) {
  throw 'Local main differs from origin/main; inspect it before merging.'
}
git merge --ff-only "origin/$source" # 有分叉时，用户确认后改为 git merge "origin/$source"
git push origin main
```

**完成标准**：`origin/main` 的 tip 含 `chore(release): 准备 X.Y.Z`。

### 2.3 打 tag 并推（需用户二次确认）

再次 `git fetch origin`，并确认 `HEAD`、`main`、`origin/main` 是**同一 SHA**；只作为 `origin/main` 的祖先并不足够，可能会把 tag 打在过期提交上。

```powershell
git fetch origin
$head = git rev-parse HEAD
$main = git rev-parse main
$originMain = git rev-parse origin/main
if (($head -ne $main) -or ($head -ne $originMain)) {
  throw 'Tag target is not the current origin/main tip.'
}
git tag vX.Y.Z
git push origin vX.Y.Z
```

**完成标准**：`origin` 上存在 `vX.Y.Z`，且 `git describe --tags --exact-match` 输出 `vX.Y.Z`。tag 推上去 `release` workflow 就开始跑，verify 阶段红了不会发包。

> 推错 tag 且尚未发包时，可 `git tag -d vX.Y.Z` 与 `git push origin :refs/tags/vX.Y.Z` 撤回后重来；包一旦推出去就撤不了。

### 2.4 盯 release workflow

tag 推上去后 `release` workflow 自动跑：**verify**（tag 落在 `main`、CHANGELOG / 前端版本 / 模板依赖版本 / 文档站徽章一致、构建、SQLite + Redis 测试（同一 SHA 的 `ci` 已把 `backend (sqlite)` 跑绿时跳过）、模板冒烟、打包 13 个 nupkg、前端 vitest + `npm pack`、抓 openapi.json）→ **publish**（OIDC 换临时 key 推 nuget.org、OIDC 发 npm、建 GitHub Release）。

```powershell
gh run list --workflow release.yml --limit 1
gh run watch <上一条输出的 run id> --exit-status
```

verify 红了不会发包，删 tag 重打即可；publish 跑完包就在 nuget.org 与 npm 上，撤不了。publish 中途红了（比如 NuGet 推完、npm 失败）先看日志定位，修好外部配置后重跑这个 job：两边都会跳过已发的版本。

**完成标准**：`release` workflow 两个 job 全绿；nuget.org 搜索 `SmartAdmin` 可见 `X.Y.Z`（索引有几分钟延迟）；`npm view smart-admin-web@X.Y.Z version` 输出 `X.Y.Z`。

---

## 3. 发布后

1. **核对 nuget.org**：`SmartAdmin`、`SmartAdmin.Templates` 等的 `X.Y.Z` 可见；空目录里 `dotnet new install SmartAdmin.Templates::X.Y.Z` 后 `dotnet new smart-app` 能还原。
2. **核对 npm**：`npm view smart-admin-web@X.Y.Z version` 输出 `X.Y.Z`；空目录里 `npx degit SmartCode-X/SmartAdmin/web/template web`、`cd web`、`npm install` 能装上新版。
3. **GitHub Release**：workflow 已自动建好，说明取自 CHANGELOG 对应段落，附 nupkg、前端 tgz 与 openapi.json；核对一眼即可，不用手工建。
4. **文档站**：`docs` workflow 在 `main` 上构建并自动部署到 GitHub Pages；部署按仓库可见性门控，仓库若转私有会退回只构建不部署，那时需要把 `site/.vitepress/dist` 发到静态托管。核对站顶导航徽章已是 `X.Y.Z`。
5. 切回 `$source` 继续开发；下一版从新的 `## Unreleased` 攒。

**完成标准**：nuget.org 与 npm 都可见 `X.Y.Z`；三个前端 version、模板依赖版本**与文档站导航徽章**均为 `X.Y.Z`；文档站线上已是新版。

---

## 禁区与提醒

- **不要在 `$source` 上打发布 tag 并推**：tag 必须落在 `origin/main` 的 tip 上。
- **不要漏 bump**；三个前端 `package.json`、模板依赖版本、lockfile、**文档站中英徽章** 必须一起改。
- **不要漏 `site/.vitepress/config.ts`**——站顶版本号靠它，不是 `web/package.json`。
- **不要在 `ci` 红着的时候推 tag**：`release` 只跑 SQLite 腿，四库矩阵、e2e 与 Docker 冒烟靠 `ci` / `docker-smoke`。
- **不要为了发版去建 NuGet API key 或 npm token，也不要在本地 `npm publish`**：发包只走 Trusted Publishing，仓库和本机都不该有长期凭据。唯一例外是包的首个版本，由用户本人手动发（见 `docs/releasing.md` 第四节）。
- 版本与「发了什么」的真源：tag + `CHANGELOG.md`。
- 更多说明见 `docs/releasing.md` 与 `CHANGELOG.md` 文首。
