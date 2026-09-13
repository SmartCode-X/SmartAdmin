# Upgrading to a New Release

The backend NuGet packages and the frontend `smart-admin-web` package share one version number. Upgrading means moving both to the same new number, installing, and reading the changelog for breaking changes. Your own pages, text and API modules live in your own repository, so an upgrade never touches them.

## Move both sides to the same number

On the backend, change the `Version` of every `SmartAdmin*` package in your project files, or in `Directory.Packages.props` if you use central package management. A project scaffolded by `dotnet new smart-app` has two: `SmartAdmin` in the main project and `SmartAdmin.Testing` under `Tests/`.

On the frontend, install the same number from `web/` (replace `X.Y.Z` with the backend packages' version); the command rewrites both `package.json` and `package-lock.json`:

```bash
npm install --save-exact smart-admin-web@X.Y.Z
```

The numbers have to match because the built-in pages in the frontend package are written against the endpoints of the same backend release. A newer frontend on an older backend ends up calling endpoints the backend doesn't have yet. That's why `package.json` pins the exact version, as the template does. `--save-exact` writes it without the `^`, so a fresh install never drifts to a newer minor on its own.

## Your app installs the peer dependencies

The kernel and your pages have to share one app instance, one router and set of stores, one set of locale messages and one icon registry. So the dependencies below may exist only once in the whole app; `smart-admin-web` declares them as peerDependencies and your `package.json` installs them (the template already lists them):

- `vue`, `vue-router`, `pinia`, `vue-i18n`
- `naive-ui`, `@vueuse/core`, `@iconify/vue`
- `smart-naive-table`, `smart-naive-icon`

With two copies installed, the kernel and your pages each talk to their own instance — a SmartTable on your page, for example, never sees the global defaults the kernel injected. Every other dependency (`echarts`, `md-editor-v3`, `@microsoft/signalr`, `openapi-fetch` and so on) comes along with the package and doesn't belong in your own `package.json`.

When a new release raises the lower bound of a peer range, `npm install` fails with `ERESOLVE`. Upgrade the packages the error names along with it. `--force` or `--legacy-peer-deps` will silence the error, but the package has only been tested inside the ranges it declares.

## After installing

1. Read this release's section in the [changelog](https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md). A release with breaking changes opens its section with a bold notice.
2. Start the backend and run `npm run gen:api` from `web/` to regenerate your own `src/api/schema.d.ts`. Wherever the contract changed, the type check in the next step points at it.
3. Run `npm run typecheck`, then click through your own pages in a browser.
4. Pages you copied into your own `views/` to override a built-in page under the same key are not updated by an upgrade. Compare them with the new release's built-in source (the file of the same name under `web/packages/admin/src/views/` in the repository) and port the changes yourself.

## The version on the login page

The login page footer shows the `version` that `main.ts` passes to `createSmartAdmin`. The template passes `__APP_VERSION__`, which is the `version` in your own `web/package.json`, injected at build time by `vite.config.ts`. It follows your app's releases; upgrading the kernel doesn't touch it.

## Tracking releases

- [Changelog](https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md): Keep a Changelog format, one section per release, covering both halves. Breaking changes are batched into the next .NET major release wherever possible.
- Packages are published only from `v*` tags on `main`: the NuGet packages to nuget.org, `smart-admin-web` to npmjs.com, under the same number. Changes on the `dev` branch don't reach any package until they are released.

Going the other way, contributing your own fixes back to SmartAdmin, is covered in the [Contributing Guide](/community/contributing).
