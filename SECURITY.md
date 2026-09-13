# Security Policy

## Reporting a vulnerability

**Do not report security vulnerabilities through public issues.** SmartAdmin ships as NuGet and npm packages and carries authentication, RBAC, and multi-org data permissions — a public report is a 0-day disclosure against every downstream consumer, before a patch exists.

Use GitHub's private vulnerability reporting: [open a security advisory](https://github.com/SmartCode-X/SmartAdmin/security/advisories/new), visible to maintainers only, so the report never reaches the public tracker. We aim to respond within **7 days**, and will coordinate the fix and disclosure timeline with you.

## Supported versions

All SmartAdmin packages ship under one version number, so a supported version means the whole set: every `SmartAdmin.*` NuGet package, the `SmartAdmin.Templates` scaffold, and the `smart-admin-web` npm package that pairs with them. The current version is whatever the latest dated entry in [CHANGELOG.md](CHANGELOG.md) is.

| Line | Supported | Notes |
|---|---|---|
| Latest minor | ✅ | Fixes land here first. |
| Previous minor | ⚠️ | Critical security fixes only, until the next minor ships. |
| Anything older | ❌ | Upgrade to a supported line first. |

The major number follows the .NET major the kernel targets, and the next major follows the next .NET LTS. A fix for a supported line goes out as a patch release on that line; we do not backport features.

The frontend kernel is the `smart-admin-web` npm package, released under the same number as the NuGet packages, so a security fix there arrives as a patch release you pick up by bumping the version. See the [upgrade guide](https://smartcode-x.github.io/SmartAdmin/guide/upgrade). The thin app template in `web/template` holds no kernel code: you copy it once and it belongs to your application from then on.
