# Component Ecosystem

Two of the frontend's business-agnostic components come from **standalone npm packages** rather than living in this repo. Neither depends on SmartAdmin. Any Vue 3 + Naive UI project can install them, and this admin console runs on those same packages rather than on a private copy.

| Package | Role | Docs |
|---|---|---|
| [`smart-naive-table`](/components/smart-table) | Column-driven data table `SmartTable`: one `columns` array drives the search form, dict rendering, and column settings; one `fetcher` adapts to any backend | [View →](/components/smart-table) |
| [`smart-naive-icon`](/components/smart-icon) | Offline-first icon renderer `SmartIcon` and picker `SmartIconPicker`, built on Iconify: multiple icon libraries, registered sets render without touching the network, single-string value | [View →](/components/smart-icon) |

> Both packages are published to npm. Looking up what a prop is called or what it defaults to? That lives with the package, in its own README. These two pages answer two questions only: whether to use it, and how to wire it into SmartAdmin.

`smart-admin-web` declares both packages as peer dependencies: the app installs one copy in its own `package.json`, and the kernel and the app share it, so there's exactly one set of table defaults and one icon registry. For how to wire them into SmartAdmin and keep theming and icons aligned, see [Theme & Icons](/frontend/appearance) and each package's own doc page above.
