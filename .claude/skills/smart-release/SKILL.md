---
name: smart-release
description: SmartAdmin 专用发版流程——定版、CHANGELOG、前端三个 package.json 与模板依赖版本 bump、文档站 site/.vitepress 导航徽章、本地验绿、合 main、打 v* tag、由 release workflow 发 NuGet 与 npm(smart-admin-web)、发布后核对。当用户在本仓要发版、发布 SmartAdmin、smart-release、打 tag、推 NuGet、发 npm、准备 X.Y.Z 时使用。勿用于其他项目。
disable-model-invocation: true
argument-hint: "[X.Y.Z]"
allowed-tools:
  - Bash(git fetch *)
  - Bash(git tag -l *)
  - Bash(git rev-parse *)
  - Bash(git merge-base *)
  - Bash(gh run list *)
  - Bash(gh run view *)
  - Bash(pwsh templates/smoke-test.ps1 *)
  - PowerShell(git fetch *)
  - PowerShell(git tag -l *)
  - PowerShell(git rev-parse *)
  - PowerShell(git merge-base *)
  - PowerShell(gh run list *)
  - PowerShell(gh run view *)
  - PowerShell(pwsh templates/smoke-test.ps1 *)
---

读仓库根目录的 `skills/smart-release.md` 并严格按它执行。它是单一真源，本文件只是入口包装。人类操作手册与节奏说明见 `docs/releasing.md`。
