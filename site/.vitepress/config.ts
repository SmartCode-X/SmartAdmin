import { defineConfig } from 'vitepress'

// 站点常量。写死的年份和 URL 会在下一年、下一次换域名时静默过期,集中在这里改一处。
const SITE_URL = 'https://smartcode-x.github.io/SmartAdmin'
// 项目页部署在子路径下。head 里手写的绝对路径 VitePress 不会替你补 base
// (themeConfig.logo 之类走 withBase 的才会),所以要用它拼。换自定义域时改成 '/'。
const BASE = '/SmartAdmin/'
const OG_IMAGE = `${SITE_URL}/icon-512.png`
const COPYRIGHT_FROM = 2025
const COPYRIGHT_YEAR = new Date().getFullYear()
const COPYRIGHT_RANGE = COPYRIGHT_YEAR > COPYRIGHT_FROM ? `${COPYRIGHT_FROM}–${COPYRIGHT_YEAR}` : `${COPYRIGHT_FROM}`

// SmartAdmin 文档门面站配置。双语:英文(默认,根路径)+ 简体中文(/zh/)。
// 部署在 GitHub Pages 的项目页(子路径)下 → base 必须是 '/SmartAdmin/',
// 否则资源会指向站点根、整站白屏。将来换成自定义域时,base 改回 '/' 并同步 SITE_URL。
// 加语种只需在 locales 里再加一块 + 对应目录。

// ── English (default locale, root path) ──
const enGuideSidebar = [
  {
    text: 'Get Started',
    items: [
      { text: 'Quick Start', link: '/guide/getting-started' },
      { text: 'The Frontend Template', link: '/guide/frontend-templates' },
      { text: 'Core Concepts', link: '/guide/concepts' },
    ],
  },
  {
    text: 'Build a Business Module',
    items: [
      { text: 'Add a Business Module (Backend)', link: '/guide/business-module' },
      { text: 'Add a Frontend Page', link: '/guide/frontend-page' },
      { text: 'Wire Import/Export on Your Entity', link: '/guide/import-export' },
      { text: 'Scheduled Jobs', link: '/guide/scheduled-jobs' },
    ],
  },
  {
    text: 'Customize the Kernel',
    items: [
      { text: 'Replace Built-in Services', link: '/guide/replace-service' },
      { text: 'Configure Multiple Databases', link: '/guide/multi-database' },
    ],
  },
  {
    text: 'Go Live',
    items: [
      { text: 'Security Baseline & Choosing a Route', link: '/guide/deployment/' },
      { text: 'Route A: Monolithic', link: '/guide/deployment/route-a' },
      { text: 'Route B: Reverse Proxy (nginx or Caddy)', link: '/guide/deployment/route-b' },
      { text: 'Route C: True Cross-Origin (CDN)', link: '/guide/deployment/route-c' },
      { text: 'Containers & Multi-Replica', link: '/guide/deployment/docker' },
    ],
  },
  {
    text: 'Help',
    items: [
      { text: 'FAQ', link: '/faq' },
      { text: 'Upgrading', link: '/guide/upgrade' },
      { text: 'Changelog', link: '/changelog' },
    ],
  },
]

const enThemeConfig = {
  nav: [
    { text: 'Guide', link: '/guide/getting-started' },
    { text: 'Backend', link: '/backend/structure' },
    { text: 'Frontend', link: '/frontend/structure' },
    { text: 'Components', link: '/components/' },
    { text: 'Standards', link: '/standard/backend' },
    { text: 'Community', link: '/community/contributing' },
    { text: '10.10.0', link: 'https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md' },
  ],
  sidebar: {
    '/guide/': enGuideSidebar,
    '/faq': enGuideSidebar,
    '/changelog': enGuideSidebar,
    '/backend/': [
      {
        text: 'Get Started',
        items: [
          { text: 'Project Structure & Startup', link: '/backend/structure' },
        ],
      },
      {
        text: 'Core Mechanisms',
        items: [
          { text: 'Architecture & Package Layering', link: '/backend/architecture' },
          { text: 'Request Pipeline', link: '/backend/request-pipeline' },
          { text: 'Multi-Org Data Permissions', link: '/backend/data-scope' },
          { text: 'Auth & Security', link: '/backend/auth-security' },
          { text: 'External Login (SSO)', link: '/backend/external-login' },
          { text: 'Realtime Notifications', link: '/backend/realtime' },
          { text: 'Event Bus', link: '/backend/event-bus' },
          { text: 'Data Layer & Auditing', link: '/backend/data-layer' },
          { text: 'Multiple Databases (ConfigId)', link: '/guide/multi-database' },
          { text: 'Error Codes', link: '/backend/error-codes' },
        ],
      },
      {
        text: 'Ops',
        items: [
          { text: 'Ops Endpoints', link: '/backend/ops' },
          { text: 'Tracing & Metrics', link: '/backend/observability' },
          { text: 'SQL Console Log', link: '/backend/sql-log' },
        ],
      },
      {
        text: 'Extensibility',
        items: [
          { text: 'Replaceability Model', link: '/backend/replaceability' },
        ],
      },
    ],
    '/frontend/': [
      {
        text: 'Get Started',
        items: [
          { text: 'Project Structure & Startup', link: '/frontend/structure' },
        ],
      },
      {
        text: 'Routing & Menus',
        items: [
          { text: 'Routing & Dynamic Menus', link: '/frontend/routing' },
          { text: 'Multi-App Portal & Router Guards', link: '/frontend/portal-guards' },
        ],
      },
      {
        text: 'Requests & Contract',
        items: [
          { text: 'HTTP Request Layer', link: '/frontend/request' },
          { text: 'Backend Contract & Error Codes', link: '/frontend/api-contract' },
        ],
      },
      {
        text: 'Features',
        items: [
          { text: 'Frontend Permissions', link: '/frontend/permission' },
          { text: 'Internationalization', link: '/frontend/i18n' },
        ],
      },
      {
        text: 'Appearance',
        items: [
          { text: 'Theme & Icons', link: '/frontend/appearance' },
        ],
      },
    ],
    '/standard/': [
      {
        text: 'Code Standards',
        items: [
          { text: 'Backend', link: '/standard/backend' },
          { text: 'Frontend', link: '/standard/frontend' },
          { text: 'Commits', link: '/standard/commit' },
        ],
      },
    ],
    '/components/': [
      {
        text: 'Components',
        items: [
          { text: 'Overview', link: '/components/' },
          { text: 'SmartTable — column-driven table', link: '/components/smart-table' },
          { text: 'SmartIcon — offline icons and picker', link: '/components/smart-icon' },
        ],
      },
    ],
    '/community/': [
      {
        text: 'Community',
        items: [
          { text: 'Contributing', link: '/community/contributing' },
          { text: 'Agent Skills & AI-Assisted Dev', link: '/community/agent-skills' },
        ],
      },
    ],
  },
  editLink: {
    pattern: 'https://github.com/SmartCode-X/SmartAdmin/edit/main/site/:path',
    text: 'Edit this page on GitHub',
  },
  footer: {
    message: 'Released under the Apache License 2.0',
    copyright: `Copyright © ${COPYRIGHT_RANGE} SmartAdmin`,
  },
}

// ── 简体中文 (/zh/) ──
const zhGuideSidebar = [
  {
    text: '上手',
    items: [
      { text: '快速开始', link: '/zh/guide/getting-started' },
      { text: '前端模板', link: '/zh/guide/frontend-templates' },
      { text: '核心概念', link: '/zh/guide/concepts' },
    ],
  },
  {
    text: '开发业务模块',
    items: [
      { text: '加一个业务模块(后端)', link: '/zh/guide/business-module' },
      { text: '加一个前端页面', link: '/zh/guide/frontend-page' },
      { text: '给自己的实体接导入导出', link: '/zh/guide/import-export' },
      { text: '定时任务', link: '/zh/guide/scheduled-jobs' },
    ],
  },
  {
    text: '定制内核',
    items: [
      { text: '替换内置服务', link: '/zh/guide/replace-service' },
      { text: '配置多数据库', link: '/zh/guide/multi-database' },
    ],
  },
  {
    text: '上线',
    items: [
      { text: '安全基线与选路线', link: '/zh/guide/deployment/' },
      { text: '路线 A:单体部署', link: '/zh/guide/deployment/route-a' },
      { text: '路线 B:反向代理(nginx 或 Caddy)', link: '/zh/guide/deployment/route-b' },
      { text: '路线 C:真跨源(CDN)', link: '/zh/guide/deployment/route-c' },
      { text: '容器化与多副本', link: '/zh/guide/deployment/docker' },
    ],
  },
  {
    text: '帮助',
    items: [
      { text: '常见问题', link: '/zh/faq' },
      { text: '升级到新版本', link: '/zh/guide/upgrade' },
      { text: '更新日志', link: '/zh/changelog' },
    ],
  },
]

const zhThemeConfig = {
  nav: [
    { text: '指南', link: '/zh/guide/getting-started' },
    { text: '后端', link: '/zh/backend/structure' },
    { text: '前端', link: '/zh/frontend/structure' },
    { text: '组件', link: '/zh/components/' },
    { text: '规范', link: '/zh/standard/backend' },
    { text: '参与', link: '/zh/community/contributing' },
    { text: '10.10.0', link: 'https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md' },
  ],
  sidebar: {
    '/zh/guide/': zhGuideSidebar,
    '/zh/faq': zhGuideSidebar,
    '/zh/changelog': zhGuideSidebar,
    '/zh/backend/': [
      {
        text: '入门',
        items: [
          { text: '项目结构与启动', link: '/zh/backend/structure' },
        ],
      },
      {
        text: '核心机制',
        items: [
          { text: '架构分层与包依赖', link: '/zh/backend/architecture' },
          { text: '请求管线', link: '/zh/backend/request-pipeline' },
          { text: '多组织数据权限', link: '/zh/backend/data-scope' },
          { text: '认证与安全', link: '/zh/backend/auth-security' },
          { text: '外部登录（SSO）', link: '/zh/backend/external-login' },
          { text: '实时通知', link: '/zh/backend/realtime' },
          { text: '事件总线', link: '/zh/backend/event-bus' },
          { text: '数据层与审计', link: '/zh/backend/data-layer' },
          { text: '多数据库（ConfigId）', link: '/zh/guide/multi-database' },
          { text: '错误码', link: '/zh/backend/error-codes' },
        ],
      },
      {
        text: '运维',
        items: [
          { text: '运维端点', link: '/zh/backend/ops' },
          { text: '追踪与指标', link: '/zh/backend/observability' },
          { text: 'SQL 控制台日志', link: '/zh/backend/sql-log' },
        ],
      },
      {
        text: '扩展',
        items: [
          { text: '可替换性模型', link: '/zh/backend/replaceability' },
        ],
      },
    ],
    '/zh/frontend/': [
      {
        text: '入门',
        items: [
          { text: '项目结构与启动', link: '/zh/frontend/structure' },
        ],
      },
      {
        text: '路由与菜单',
        items: [
          { text: '路由与动态菜单', link: '/zh/frontend/routing' },
          { text: '多应用门户与路由守卫', link: '/zh/frontend/portal-guards' },
        ],
      },
      {
        text: '请求与契约',
        items: [
          { text: 'HTTP 请求层', link: '/zh/frontend/request' },
          { text: '对接后端:响应契约与错误码', link: '/zh/frontend/api-contract' },
        ],
      },
      {
        text: '功能',
        items: [
          { text: '前端权限', link: '/zh/frontend/permission' },
          { text: '国际化', link: '/zh/frontend/i18n' },
        ],
      },
      {
        text: '外观',
        items: [
          { text: '主题与图标', link: '/zh/frontend/appearance' },
        ],
      },
    ],
    '/zh/standard/': [
      {
        text: '代码规范',
        items: [
          { text: '后端规范', link: '/zh/standard/backend' },
          { text: '前端规范', link: '/zh/standard/frontend' },
          { text: '提交规范', link: '/zh/standard/commit' },
        ],
      },
    ],
    '/zh/components/': [
      {
        text: '组件',
        items: [
          { text: '概览', link: '/zh/components/' },
          { text: 'SmartTable — 列驱动表格', link: '/zh/components/smart-table' },
          { text: 'SmartIcon — 离线图标与选择器', link: '/zh/components/smart-icon' },
        ],
      },
    ],
    '/zh/community/': [
      {
        text: '参与',
        items: [
          { text: '贡献指南', link: '/zh/community/contributing' },
          { text: 'Agent Skills 与 AI 辅助开发', link: '/zh/community/agent-skills' },
        ],
      },
    ],
  },
  editLink: {
    pattern: 'https://github.com/SmartCode-X/SmartAdmin/edit/main/site/:path',
    text: '在 GitHub 上编辑本页',
  },
  footer: {
    message: '基于 Apache License 2.0 开源',
    copyright: `Copyright © ${COPYRIGHT_RANGE} SmartAdmin`,
  },
  docFooter: {
    prev: '上一页',
    next: '下一页',
  },
  outline: { label: '本页目录' },
  lastUpdatedText: '最后更新',
  returnToTopLabel: '返回顶部',
  darkModeSwitchLabel: '外观',
  sidebarMenuLabel: '菜单',
}

export default defineConfig({
  base: BASE,
  title: 'SmartAdmin',
  lastUpdated: true,
  cleanUrls: true,
  // 站点地图:VitePress 按最终产出的路由生成,双语两侧都收进去,不用手工维护清单
  // 末尾斜杠不能省:sitemap 用 new URL(相对路径, hostname) 拼,
  // 没有斜杠时 'SmartAdmin' 会被当成文件名替换掉,整份 sitemap 丢掉子路径。
  sitemap: { hostname: `${SITE_URL}/` },
  head: [
    ['link', { rel: 'icon', href: `${BASE}icon-128.png` }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:site_name', content: 'SmartAdmin' }],
    ['meta', { property: 'og:image', content: OG_IMAGE }],
    ['meta', { property: 'og:url', content: SITE_URL }],
    ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
    ['meta', { name: 'twitter:image', content: OG_IMAGE }],
  ],
  // 每页的 og:title / og:description 用该页自己的标题与描述,不然所有分享卡片长得一模一样
  transformPageData(pageData) {
    const title = pageData.frontmatter.title ?? pageData.title ?? 'SmartAdmin'
    const description = pageData.frontmatter.description ?? pageData.description ?? ''
    pageData.frontmatter.head ??= []
    pageData.frontmatter.head.push(
      ['meta', { property: 'og:title', content: title }],
      ['meta', { name: 'twitter:title', content: title }],
    )
    if (description) {
      pageData.frontmatter.head.push(
        ['meta', { property: 'og:description', content: description }],
        ['meta', { name: 'twitter:description', content: description }],
      )
    }
  },
  themeConfig: {
    logo: '/icon-128.png',
    socialLinks: [
      { icon: 'github', link: 'https://github.com/SmartCode-X/SmartAdmin' },
    ],
    search: {
      provider: 'local',
    },
  },
  locales: {
    root: {
      label: 'English',
      lang: 'en',
      description: 'The replaceable admin kernel for .NET: install and go, upgrade by bumping a version.',
      themeConfig: enThemeConfig,
    },
    zh: {
      label: '简体中文',
      lang: 'zh-CN',
      link: '/zh/',
      description: '可替换的企业后台内核：装上即用，升级只改版本号。',
      themeConfig: zhThemeConfig,
    },
  },
})
