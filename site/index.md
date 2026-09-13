---
layout: home

hero:
  name: SmartAdmin
  text: The replaceable admin kernel for .NET
  tagline: Install and go. Upgrade by bumping a version.
  image:
    src: /icon-128.png
    alt: SmartAdmin
  actions:
    - theme: brand
      text: Get Started
      link: /guide/getting-started
    - theme: alt
      text: GitHub
      link: https://github.com/SmartCode-X/SmartAdmin

features:
  - icon: 🧩
    title: Pluggable Architecture
    details: Every built-in service is interface-registered and overridable step by step — swap any piece without forking, upgrade without conflicts.
  - icon: 🏢
    title: Multi-Org Data Permissions
    details: Five built-in data scopes, enforced automatically via ORM global filters — business queries never need manual org-filter conditions.
  - icon: ⚡
    title: Zero-Config Startup
    details: SQLite by default auto-creates tables and writes seed data, printing the super-admin password once on first startup; switching databases is a single config change.
  - icon: 📦
    title: Minimal Dependencies
    details: Runtime depends only on SqlSugar and Microsoft.* official libraries — Redis, object storage, etc. are opt-in.
  - icon: 🔐
    title: Auth & Security
    details: JWT auth, login lockout, rate limiting, forced logout, and log redaction are on by default; three CAPTCHA styles ship built in and switch on when you want them.
  - icon: 🖥️
    title: Full-Stack Delivery
    details: The frontend kernel is the npm package smart-admin-web, versioned in lockstep with the NuGet packages; your app starts from a thin shell template (Vue 3 + Naive UI). Containerized deployment and multi-replica horizontal scaling are supported.
  - icon: 🧰
    title: Component Ecosystem
    details: Shared components like SmartTable and SmartIconPicker are published as standalone npm packages — install them individually into any Vue 3 + Naive UI project.
    link: /components/
    linkText: Browse the components
  - icon: 🤖
    title: Assisted-Development Skills
    details: Workflows like adding entities, scaffolding CRUD, and swapping services are written up as standard skills — AI assistants or developers follow them to generate standards-compliant code.
    link: /community/agent-skills
    linkText: Browse the skills
