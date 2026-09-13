English | [简体中文](https://github.com/SmartCode-X/SmartAdmin/blob/main/README.md)

<div align="center">

<img src="https://raw.githubusercontent.com/SmartCode-X/SmartAdmin/main/site/public/icon-512.png" width="100" alt="SmartAdmin">

# SmartAdmin

*The replaceable, modern admin kernel for .NET. Simple, efficient, AI-assisted development. Install and go, upgrade by bumping a version.*

[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](https://github.com/SmartCode-X/SmartAdmin/blob/main/LICENSE)
[![Stars](https://img.shields.io/github/stars/SmartCode-X/SmartAdmin?style=social)](https://github.com/SmartCode-X/SmartAdmin/stargazers)
[![Forks](https://img.shields.io/github/forks/SmartCode-X/SmartAdmin?style=social)](https://github.com/SmartCode-X/SmartAdmin/forks)
[![NuGet](https://img.shields.io/nuget/v/SmartAdmin)](https://www.nuget.org/packages/SmartAdmin)
[![npm](https://img.shields.io/npm/v/smart-admin-web)](https://www.npmjs.com/package/smart-admin-web)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![Build](https://img.shields.io/github/actions/workflow/status/SmartCode-X/SmartAdmin/ci.yml?branch=main&event=push&label=build)](https://github.com/SmartCode-X/SmartAdmin/actions/workflows/ci.yml?query=branch%3Amain+event%3Apush)

**[📖 Docs](https://smartcode-x.github.io/SmartAdmin/) · [🚀 Quick Start](https://smartcode-x.github.io/SmartAdmin/guide/getting-started) · [📋 Changelog](https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md)**

</div>

---

## What is it

SmartAdmin is a ready-made enterprise back-office foundation for .NET 10 / ASP.NET Core projects. Login, users, roles, menu permissions, organizations and data permissions, dictionaries, config, logs, files and scheduled jobs are the features every back office has to build; they are already done and ship, admin UI included, as two packages:

- Backend: the NuGet package `SmartAdmin`
- Frontend: the npm package `smart-admin-web` (Vue 3 + Naive UI)

Install them and you have a complete back office with login, user and permission management; from there you only write your own business code. The kernel's code never enters your repository, upgrading means moving both packages to the same new version, and any built-in piece that doesn't fit can be swapped for your own implementation.

## Quick start

You need the .NET 10 SDK and Node.js 22.12 or later.

**Step 1: generate the backend and run it**

```bash
dotnet new install SmartAdmin.Templates
dotnet new smart-app -n MyApp
cd MyApp
dotnet run
```

The backend listens on `http://localhost:5100` and uses SQLite by default, creating its tables on first start. The console prints the super-admin account `superAdmin` and a random password. The password is shown only this once, so note it down.

**Step 2: pull the frontend and run it**

In a second terminal, still in `MyApp`:

```bash
npx degit SmartCode-X/SmartAdmin/web/template web
cd web
npm install
npm run dev
```

Open `http://localhost:5173` and sign in with the account from step 1.

**Existing ASP.NET Core project**: install the meta-package, then register it in `Program.cs`.

```bash
dotnet add package SmartAdmin
```

```csharp
builder.Services.AddSmartAdmin(builder.Configuration,
    o => o.ApplicationAssemblies.Add(typeof(Program).Assembly)); // entities here get tables, controllers get routes
var app = builder.Build();
app.MapSmartAdmin();
app.Run();
```

Pull the frontend as in step 2. Production requires a JWT signing key, `SmartAdmin:Jwt:SecretKey`; the rest is in [Deployment](https://smartcode-x.github.io/SmartAdmin/guide/deployment/).

## Next steps

| I want to | Read |
|---|---|
| Add a business module (table, API, permissions) | [Add a Business Module](https://smartcode-x.github.io/SmartAdmin/guide/business-module) |
| Build its admin page | [Add a Frontend Page](https://smartcode-x.github.io/SmartAdmin/guide/frontend-page) |
| Switch to MySQL / SQL Server / PostgreSQL | [Swap out the default database](https://smartcode-x.github.io/SmartAdmin/guide/getting-started#swap-out-the-default-database) |
| Change built-in behavior (login flow, password hashing, caching…) | [Replace Built-in Services](https://smartcode-x.github.io/SmartAdmin/guide/replace-service) |
| Add scheduled jobs, wire import/export | [Scheduled Jobs](https://smartcode-x.github.io/SmartAdmin/guide/scheduled-jobs) · [Import/Export](https://smartcode-x.github.io/SmartAdmin/guide/import-export) |
| Deploy, upgrade | [Deployment](https://smartcode-x.github.io/SmartAdmin/guide/deployment/) · [Upgrading](https://smartcode-x.github.io/SmartAdmin/guide/upgrade) |
| Understand the design | [Core Concepts](https://smartcode-x.github.io/SmartAdmin/guide/concepts) · [Runtime architecture diagram](https://github.com/SmartCode-X/SmartAdmin/blob/main/docs/architecture/smart-runtime.en.svg) |
| Let an AI assistant write code by the rules | [Agent Skills](https://smartcode-x.github.io/SmartAdmin/community/agent-skills) |

## Built-in features

- **Authentication**: account and password, JWT with refresh tokens, login lockout, online sessions and forced logout, password policy; optional captcha, SMS or TOTP second factor, and third-party login (OIDC built in; WeCom, DingTalk, WeChat and GitHub as optional packages)
- **Permissions**: roles and three-level menus (directory / page / button), where a permission code is the API route; five organization data scopes that filter queries automatically, so business code writes no org conditions
- **Administration**: organizations, positions, users, a multi-app portal, dictionaries, a config center, notices and a recycle bin
- **Operations**: operation / login / exception logs, file uploads (resumable chunks, signed links), scheduled jobs (cron, fixed interval, one-off), server monitoring and health checks
- **Databases**: SQLite, MySQL, SQL Server and PostgreSQL, switched by one config section; optional Redis for multi-replica deployments
- **Admin UI**: dynamic menu routing, button-level permissions, light and dark themes, three login skins, Chinese and English, plus common components for tables, forms, dictionaries, uploads and an import wizard

## Packages

| Package | Description |
|---|---|
| `SmartAdmin` | Backend meta-package; install this one and you get the four kernel packages below |
| `SmartAdmin.Core` / `.SqlSugar` / `.Services` / `.AspNetCore` | Kernel layers: contracts, data layer, business services, ASP.NET Core integration |
| `SmartAdmin.Excel` | Optional: xlsx import and export |
| `SmartAdmin.Caching.Redis` | Optional: Redis cache, sessions shared across replicas |
| `SmartAdmin.Auth.WeCom` / `.DingTalk` / `.WeChat` / `.GitHub` | Optional: third-party login |
| `SmartAdmin.Testing` | For test projects: boots the whole host, test databases for all four dialects |
| `SmartAdmin.Templates` | The `dotnet new smart-app` project template |
| `smart-admin-web` (npm) | The admin UI: layout, dynamic routing, every built-in page and component |

All packages share one version number, and frontend and backend use the same number; the major version follows the .NET major version (10.x targets .NET 10).

## Contributing and license

Issues and PRs are welcome. Local development and running the tests are covered in the [Contributing Guide](https://smartcode-x.github.io/SmartAdmin/community/contributing); report security issues as described in [SECURITY.md](https://github.com/SmartCode-X/SmartAdmin/blob/main/SECURITY.md).

[Apache License 2.0](https://github.com/SmartCode-X/SmartAdmin/blob/main/LICENSE)
