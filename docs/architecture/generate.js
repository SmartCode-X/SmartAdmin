// 运行时架构图生成器：手写 SVG（配色取自 web/packages/admin/src/styles/tokens.css 深色主题）。
// 改文字 / 节点改这里的 TEXT，重跑即可：
//   node docs/architecture/generate.js

const fs = require('fs');
const path = require('path');

const W = 1600, H = 1080;
const FONT = "Inter, 'Segoe UI', 'PingFang SC', 'Microsoft YaHei', 'Noto Sans SC', system-ui, sans-serif";
const MONO = "'JetBrains Mono', 'Cascadia Code', Consolas, 'SFMono-Regular', Menlo, monospace";
const esc = s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const isCjk = ch => /[⺀-鿿豈-﫿＀-￯]/.test(ch);
const textW = (text, latin, cjk) => [...text].reduce((w, ch) => w + (isCjk(ch) ? cjk : latin), 0);

// neon theme on top of tokens.css dark values
const S = {
  bg: ['#02040f', '#070d1f', '#02040f'], card: '#0a1224', cardAlt: '#060b18', codeBg: '#02040f',
  border: 'rgba(56,189,248,0.22)', text: '#f8fafc', text2: '#c7d2fe', text3: '#8b9bb8', text4: '#5b6b8a',
  teal: '#2dd4bf', tealHi: '#5eead4', indigo: '#818cf8', sky: '#38bdf8', amber: '#fbbf24',
  dots: '#38bdf8', dotsOp: 0.14, shadow: '#000000', shadowOp: 0.7, sheen: 0.07, glow: 0.22, badgeText: '#02040f',
};
const col = name => ({ indigo: S.indigo, teal: S.teal, sky: S.sky, amber: S.amber }[name] || name);

const TEXT = {
  'zh-CN': {
    title: 'SmartAdmin 运行时架构',
    subtitle: '一次管理端请求自上而下走过的四层，以及每层对应的包',
    chips: ['.NET 10 · ASP.NET Core', 'SqlSugar', 'Vue 3 · Naive UI'],
    layers: [
      { name: '前端层', idx: '01', pkg: 'smart-admin-web', pkgSub: 'npm 包 · 与 NuGet 同号', color: 'indigo', nodes: [
        { title: '管理端 SPA', sub: ['Vue 3 + Naive UI', '动态路由 · v-auth 按钮权限'], tag: ':5173', color: 'indigo' },
        { title: '组件库', sub: ['SmartTable · FormContainer', '字典组件 · 导入向导 · CronEditor'], tag: 'COMPONENTS.md', color: 'indigo' },
        { title: 'API 契约', sub: ['/openapi/v1.json', '→ schema.d.ts 端到端类型'], tag: 'npm run gen:api', color: 'indigo', dashed: true }] },
      { name: '宿主层', idx: '02', pkg: 'SmartAdmin.AspNetCore', pkgSub: 'AddSmartAdmin / MapSmartAdmin', color: 'teal', nodes: [
        { title: '控制器 + 过滤器', sub: ['结果信封 · 异常过滤', '操作日志 · 限流'], tag: ':5100', color: 'teal' },
        { title: '鉴权管道', sub: ['JWT → RolePermission', '→ DataScope 数据范围'], tag: '权限码 = 路由', color: 'sky' },
        { title: '外部登录', sub: ['OIDC · 企微 · 钉钉', 'GitHub · 微信'], tag: 'IExternalAuthProvider', color: 'sky' },
        { title: '实时推送', sub: ['SignalR Hub', '强制下线 · 通知公告'], tag: '可选开启', color: 'sky', dashed: true }] },
      { name: '领域层', idx: '03', pkg: 'SmartAdmin.Services', pkgSub: '实体 + 全部业务服务', color: 'teal', nodes: [
        { title: '领域服务', sub: ['用户 · 角色 · 菜单 · 机构', '字典 · 配置 · 日志 · 文件'], tag: 'TryAdd 可替换', color: 'teal' },
        { title: '缓存', sub: ['Memory / Redis', '会话 · 权限 · 字典'], tag: 'ICacheProvider', color: 'amber' },
        { title: '定时任务调度', sub: ['cron · 固定间隔 · 一次性', 'IAdminJob · HTTP · SQL'], tag: '内核自带', color: 'teal' },
        { title: '可选 Worker', sub: ['AddSmartAdminWorker', 'API 下线时任务继续跑'], tag: '可选', color: 'teal', dashed: true }] },
      { name: '数据层', idx: '04', pkg: 'SmartAdmin.SqlSugar', pkgSub: 'CodeFirst · 种子 · 全局过滤器', color: 'amber', nodes: [
        { title: 'SqlSugar', sub: ['仓储 · CodeFirst 建表', '软删 / 数据范围全局过滤'], tag: '单例 Scope', color: 'teal' },
        { title: '数据库', sub: ['SQLite · MySQL', 'SQL Server · PostgreSQL'], tag: '配置切换', color: 'amber' },
        { title: '文件存储', sub: ['上传目录 · 分片续传', '签名直链 · 软删回收'], tag: 'Upload:RootPath', color: 'amber' }] },
    ],
    layerEdges: ['HTTPS /api', '已授权', '查询 / 写入'],
    core: 'SmartAdmin.Core 契约层：接口 · Options · Result<T> · ErrorCode · 雪花 ID  ·  零运行时依赖，四层都建在它上面',
    code: '装上即用',
    pkg: { title: 'NuGet 包分层 · 依赖只向下', line1: '元包 SmartAdmin 一次装全 · 运行时只依赖 SqlSugarCore + Microsoft.*', line2: '可选包：Excel · Caching.Redis · Auth.WeCom / DingTalk / GitHub / WeChat', chain: 'Core → SqlSugar → Services → AspNetCore' },
    legend: [['indigo', '前端'], ['teal', '内核'], ['sky', '鉴权'], ['amber', '存储'], ['dash', '虚线 = 可选 / 旁路']],
  },
  en: {
    title: 'SmartAdmin Runtime Architecture',
    subtitle: 'The four layers one admin request passes through, top to bottom, and the package behind each layer',
    chips: ['.NET 10 · ASP.NET Core', 'SqlSugar', 'Vue 3 · Naive UI'],
    layers: [
      { name: 'Frontend', idx: '01', pkg: 'smart-admin-web', pkgSub: 'npm package · same version as NuGet', color: 'indigo', nodes: [
        { title: 'Admin SPA', sub: ['Vue 3 + Naive UI', 'dynamic routes · v-auth'], tag: ':5173', color: 'indigo' },
        { title: 'Components', sub: ['SmartTable · FormContainer', 'dict set · ImportWizard · CronEditor'], tag: 'COMPONENTS.md', color: 'indigo' },
        { title: 'API contract', sub: ['/openapi/v1.json', '→ schema.d.ts, typed end to end'], tag: 'npm run gen:api', color: 'indigo', dashed: true }] },
      { name: 'Host', idx: '02', pkg: 'SmartAdmin.AspNetCore', pkgSub: 'AddSmartAdmin / MapSmartAdmin', color: 'teal', nodes: [
        { title: 'Controllers + filters', sub: ['result envelope · exceptions', 'operation log · rate limit'], tag: ':5100', color: 'teal' },
        { title: 'Auth pipeline', sub: ['JWT → RolePermission', '→ DataScope'], tag: 'permission = route', color: 'sky' },
        { title: 'External login', sub: ['OIDC · WeCom · DingTalk', 'GitHub · WeChat'], tag: 'IExternalAuthProvider', color: 'sky' },
        { title: 'Real-time push', sub: ['SignalR Hub', 'force logout · notices'], tag: 'optional', color: 'sky', dashed: true }] },
      { name: 'Domain', idx: '03', pkg: 'SmartAdmin.Services', pkgSub: 'entities + every business service', color: 'teal', nodes: [
        { title: 'Domain services', sub: ['users · roles · menus · orgs', 'dicts · config · logs · files'], tag: 'TryAdd replaceable', color: 'teal' },
        { title: 'Cache', sub: ['Memory / Redis', 'sessions · permissions · dicts'], tag: 'ICacheProvider', color: 'amber' },
        { title: 'Job scheduler', sub: ['cron · interval · one-shot', 'IAdminJob · HTTP · SQL'], tag: 'in-kernel', color: 'teal' },
        { title: 'Optional worker', sub: ['AddSmartAdminWorker', 'jobs keep running if API is down'], tag: 'optional', color: 'teal', dashed: true }] },
      { name: 'Data', idx: '04', pkg: 'SmartAdmin.SqlSugar', pkgSub: 'CodeFirst · seeds · global filters', color: 'amber', nodes: [
        { title: 'SqlSugar', sub: ['repository · CodeFirst tables', 'soft-delete / data-scope filters'], tag: 'singleton scope', color: 'teal' },
        { title: 'Database', sub: ['SQLite · MySQL', 'SQL Server · PostgreSQL'], tag: 'switch by config', color: 'amber' },
        { title: 'File storage', sub: ['upload root · chunked resume', 'signed links · soft-delete reclaim'], tag: 'Upload:RootPath', color: 'amber' }] },
    ],
    layerEdges: ['HTTPS /api', 'authorized', 'query / write'],
    core: 'SmartAdmin.Core contracts: interfaces · Options · Result<T> · ErrorCode · snowflake IDs  ·  no runtime deps; all four layers build on it',
    code: 'Install and go',
    pkg: { title: 'NuGet package layers · dependencies point down only', line1: 'Meta-package SmartAdmin installs all four · runtime deps are only SqlSugarCore + Microsoft.*', line2: 'Optional: Excel · Caching.Redis · Auth.WeCom / DingTalk / GitHub / WeChat', chain: 'Core → SqlSugar → Services → AspNetCore' },
    legend: [['indigo', 'frontend'], ['teal', 'kernel'], ['sky', 'auth'], ['amber', 'storage'], ['dash', 'dashed = optional / side path']],
  },
};

function defs() {
  const markers = [S.teal, S.indigo, S.sky, S.amber].map(c => `<marker id="arr-${c.slice(1)}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse"><path d="M0 0L10 5L0 10z" fill="${c}"/></marker>`).join('');
  return `<defs>
  <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${S.bg[0]}"/><stop offset="0.55" stop-color="${S.bg[1]}"/><stop offset="1" stop-color="${S.bg[2]}"/></linearGradient>
  <radialGradient id="glow" cx="0.5" cy="0.4" r="0.55"><stop offset="0" stop-color="${S.teal}" stop-opacity="${S.glow}"/><stop offset="1" stop-color="${S.teal}" stop-opacity="0"/></radialGradient>
  <radialGradient id="glow2" cx="0.1" cy="0.1" r="0.45"><stop offset="0" stop-color="${S.indigo}" stop-opacity="${S.glow}"/><stop offset="1" stop-color="${S.indigo}" stop-opacity="0"/></radialGradient>
  <linearGradient id="sheen" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff" stop-opacity="${S.sheen}"/><stop offset="0.4" stop-color="#ffffff" stop-opacity="0"/></linearGradient>
  <pattern id="dots" width="28" height="28" patternUnits="userSpaceOnUse"><circle cx="1" cy="1" r="1" fill="${S.dots}" fill-opacity="${S.dotsOp}"/></pattern>
  <filter id="shadow" x="-10%" y="-10%" width="120%" height="135%"><feDropShadow dx="0" dy="6" stdDeviation="8" flood-color="${S.shadow}" flood-opacity="${S.shadowOp}"/></filter>
  <filter id="neon" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="6" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
  <linearGradient id="gTile" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#7C83FF"/><stop offset="1" stop-color="#4B52E6"/></linearGradient>
  ${markers}
</defs>`;
}

const mark = (x, y, scale) => `<g transform="translate(${x},${y}) scale(${scale})"><rect width="120" height="120" rx="27" fill="url(#gTile)"/><polygon points="60,20 94.6,40 94.6,80 60,100 25.4,80 25.4,40" fill="none" stroke="#fff" stroke-width="11" stroke-linejoin="round"/><path d="M60 60L60 39M60 60L78.2 49.5M60 60L78.2 70.5M60 60L60 81M60 60L41.8 70.5M60 60L41.8 49.5" fill="none" stroke="#fff" stroke-width="3.5" stroke-linecap="round"/><circle cx="60" cy="39" r="5" fill="#fff"/><circle cx="78.2" cy="49.5" r="5" fill="#fff"/><circle cx="78.2" cy="70.5" r="5" fill="#fff"/><circle cx="60" cy="81" r="5" fill="#fff"/><circle cx="41.8" cy="70.5" r="5" fill="#fff"/><circle cx="41.8" cy="49.5" r="5" fill="#fff"/><circle cx="60" cy="60" r="8.5" fill="#A5F3FC"/></g>`;

function header(t) {
  let s = mark(100, 38, 0.46);
  s += `<text x="170" y="70" font-family="${FONT}" font-size="30" font-weight="700" fill="${S.text}">${esc(t.title)}</text>`;
  s += `<text x="170" y="98" font-family="${FONT}" font-size="15" fill="${S.text3}">${esc(t.subtitle)}</text>`;
  let chipX = W - 100;
  for (const chip of [...t.chips].reverse()) {
    const w = textW(chip, 8, 13) + 28; chipX -= w;
    s += `<rect x="${chipX}" y="50" width="${w}" height="30" rx="15" fill="${S.card}" stroke="${S.border}"/><text x="${chipX + w / 2}" y="70" text-anchor="middle" font-family="${MONO}" font-size="13" fill="${S.text2}">${esc(chip)}</text>`;
    chipX -= 12;
  }
  s += `<line x1="100" y1="122" x2="${W - 100}" y2="122" stroke="${S.border}"/>`;
  return s;
}

function node(x, y, w, h, n) {
  const c = col(n.color);
  const dash = n.dashed ? ' stroke-dasharray="7 5"' : '';
  const subs = n.sub.map((sub, i) => `<text x="${x + w / 2}" y="${y + 57 + i * 18}" text-anchor="middle" font-family="${FONT}" font-size="13.5" fill="${S.text2}">${esc(sub)}</text>`).join('');
  const tagW = Math.max(52, textW(n.tag, 7.3, 12) + 20);
  const tag = `<rect x="${x + w / 2 - tagW / 2}" y="${y + h - 30}" width="${tagW}" height="22" rx="11" fill="${S.cardAlt}" stroke="${c}" stroke-opacity="0.5"/><text x="${x + w / 2}" y="${y + h - 15}" text-anchor="middle" font-family="${MONO}" font-size="12" fill="${c}">${esc(n.tag)}</text>`;
  return `<g filter="url(#shadow)"><rect x="${x}" y="${y}" width="${w}" height="${h}" rx="12" fill="${S.card}" stroke="${c}" stroke-width="1.5" stroke-opacity="0.9"${dash}/></g>` +
    `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="12" fill="none" stroke="${c}" stroke-width="2" stroke-opacity="0.5" filter="url(#neon)"${dash}/>` +
    `<rect x="${x + 1}" y="${y + 1}" width="${w - 2}" height="${h - 2}" rx="11" fill="url(#sheen)"/>` +
    `<circle cx="${x + 18}" cy="${y + 18}" r="4" fill="${c}"/>` +
    `<text x="${x + w / 2}" y="${y + 36}" text-anchor="middle" font-family="${FONT}" font-size="17" font-weight="700" fill="${S.text}">${esc(n.title)}</text>` + subs + tag;
}

function label(x, y, text, c) {
  const w = textW(text, 7.5, 12.5) + 18;
  return `<rect x="${x - w / 2}" y="${y - 11}" width="${w}" height="22" rx="6" fill="${S.bg[0]}" fill-opacity="0.94" stroke="${c}" stroke-opacity="0.4"/><text x="${x}" y="${y + 4.5}" text-anchor="middle" font-family="${MONO}" font-size="12.5" fill="${c}">${esc(text)}</text>`;
}

function arrow(d, c, dashed = false) {
  return `<path d="${d}" fill="none" stroke="${c}" stroke-width="7" stroke-opacity="0.4" filter="url(#neon)"${dashed ? ' stroke-dasharray="6 5"' : ''}/>` +
    `<path d="${d}" fill="none" stroke="${c}" stroke-width="2.5"${dashed ? ' stroke-dasharray="6 5"' : ''} marker-end="url(#arr-${c.slice(1)})"/>`;
}

function layered(t) {
  let s = '';
  const bx = 100, bw = 1400, by0 = 150, bh = 136, gap = 16;
  const labelW = 100, pkgW = 230, pkgGap = 24, nodeH = 112, nGap = 20;
  const contentX = bx + labelW + 16, contentW = bw - labelW - 16 - pkgW - pkgGap - 16;
  const widthFor = n => Math.min(300, (contentW - (n - 1) * nGap) / n);
  t.layers.forEach((L, li) => {
    const y = by0 + li * (bh + gap), c = col(L.color);
    s += `<rect x="${bx}" y="${y}" width="${bw}" height="${bh}" rx="16" fill="${c}" fill-opacity="0.05" stroke="${c}" stroke-opacity="0.35"/>`;
    s += `<rect x="${bx}" y="${y}" width="6" height="${bh}" rx="3" fill="${c}"/>`;
    s += `<text x="${bx + 24}" y="${y + bh / 2 - 4}" font-family="${FONT}" font-size="19" font-weight="700" fill="${S.text}">${esc(L.name)}</text>`;
    s += `<text x="${bx + 24}" y="${y + bh / 2 + 18}" font-family="${MONO}" font-size="12" fill="${c}">${L.idx}</text>`;
    const nw = widthFor(L.nodes.length), ny = y + (bh - nodeH) / 2;
    L.nodes.forEach((nd, i) => { s += node(contentX + i * (nw + nGap), ny, nw, nodeH, nd); });
    const px = bx + bw - pkgW - 16, py = y + (bh - 72) / 2;
    s += `<rect x="${px}" y="${py}" width="${pkgW}" height="72" rx="12" fill="${S.cardAlt}" stroke="${c}" stroke-opacity="0.65" stroke-width="1.5"/>`;
    s += `<text x="${px + pkgW / 2}" y="${py + 30}" text-anchor="middle" font-family="${MONO}" font-size="13.5" font-weight="700" fill="${S.text}">${esc(L.pkg)}</text>`;
    s += `<text x="${px + pkgW / 2}" y="${py + 52}" text-anchor="middle" font-family="${FONT}" font-size="12" fill="${S.text3}">${esc(L.pkgSub)}</text>`;
    if (li < t.layers.length - 1) {
      const nextW = widthFor(t.layers[li + 1].nodes.length);
      const ax = contentX + Math.min(nw, nextW) / 2, y1 = ny + nodeH, y2 = y + bh + gap + (bh - nodeH) / 2;
      const c2 = li === 0 ? S.indigo : S.teal;
      s += arrow(`M${ax} ${y1}L${ax} ${y2 - 1}`, c2);
      s += label(ax + textW(t.layerEdges[li], 7.5, 12.5) / 2 + 26, (y1 + y2) / 2, t.layerEdges[li], c2);
    }
  });
  // core band
  const cy = by0 + 4 * (bh + gap);
  s += `<rect x="${bx}" y="${cy}" width="${bw}" height="48" rx="12" fill="${S.cardAlt}" stroke="${S.teal}" stroke-opacity="0.55" stroke-dasharray="8 6"/>`;
  s += `<text x="${bx + bw / 2}" y="${cy + 30}" text-anchor="middle" font-family="${MONO}" font-size="13.5" fill="${S.text2}">${esc(t.core)}</text>`;
  // bottom cards
  const qy = cy + 70, qh = 184;
  const qx = 100, qw = 640;
  s += `<g filter="url(#shadow)"><rect x="${qx}" y="${qy}" width="${qw}" height="${qh}" rx="14" fill="${S.card}" stroke="${S.border}"/></g>`;
  s += `<text x="${qx + 24}" y="${qy + 36}" font-family="${FONT}" font-size="16" font-weight="700" fill="${S.text}">${esc(t.code)}</text>`;
  s += `<rect x="${qx + 24}" y="${qy + 54}" width="${qw - 48}" height="${qh - 78}" rx="10" fill="${S.codeBg}" stroke="${S.border}"/>`;
  const code = [
    [['builder.Services.', '#cbd5e1'], ['AddSmartAdmin', S.tealHi], ['(builder.Configuration);', '#cbd5e1']],
    [['var app = builder.Build();', '#cbd5e1']],
    [['app.', '#cbd5e1'], ['MapSmartAdmin', S.tealHi], ['();', '#cbd5e1']],
  ];
  code.forEach((parts, i) => {
    const inner = parts.map(([txt, c]) => `<tspan fill="${c}">${esc(txt)}</tspan>`).join('');
    s += `<text x="${qx + 44}" y="${qy + 88 + i * 30}" font-family="${MONO}" font-size="15" xml:space="preserve">${inner}</text>`;
  });
  const ox = 780, ow = 720;
  s += `<g filter="url(#shadow)"><rect x="${ox}" y="${qy}" width="${ow}" height="${qh}" rx="14" fill="${S.card}" stroke="${S.border}"/></g>`;
  s += `<text x="${ox + 24}" y="${qy + 36}" font-family="${FONT}" font-size="16" font-weight="700" fill="${S.text}">${esc(t.pkg.title)}</text>`;
  s += `<text x="${ox + 24}" y="${qy + 76}" font-family="${FONT}" font-size="14" fill="${S.text2}">${esc(t.pkg.line1)}</text>`;
  s += `<text x="${ox + 24}" y="${qy + 106}" font-family="${FONT}" font-size="14" fill="${S.text3}">${esc(t.pkg.line2)}</text>`;
  s += `<text x="${ox + 24}" y="${qy + 140}" font-family="${MONO}" font-size="12.5" fill="${S.text4}">${esc(t.pkg.chain)}</text>`;
  // legend
  let lx = 100; const ly = qy + qh + 40;
  for (const [k, name] of t.legend) {
    if (k === 'dash') s += `<line x1="${lx}" y1="${ly}" x2="${lx + 28}" y2="${ly}" stroke="${S.text3}" stroke-width="2" stroke-dasharray="6 5"/>`;
    else s += `<rect x="${lx}" y="${ly - 8}" width="28" height="16" rx="4" fill="${S.card}" stroke="${col(k)}" stroke-width="1.5"/>`;
    lx += 38;
    s += `<text x="${lx}" y="${ly + 5}" font-family="${FONT}" font-size="13.5" fill="${S.text3}">${esc(name)}</text>`;
    lx += textW(name, 7.4, 13.5) + 30;
  }
  s += `<text x="${W - 100}" y="${ly + 5}" text-anchor="end" font-family="${MONO}" font-size="12.5" fill="${S.text4}">github.com/SmartCode-X/SmartAdmin</text>`;
  return s;
}

function build(t) {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" role="img" aria-label="${esc(t.title)}">${defs()}` +
    `<rect width="${W}" height="${H}" fill="url(#bg)"/><rect width="${W}" height="${H}" fill="url(#dots)"/><rect width="${W}" height="${H}" fill="url(#glow)"/><rect width="${W}" height="${H}" fill="url(#glow2)"/>` +
    header(t) + layered(t) + '</svg>\n';
}

const docsDir = process.argv[2] ?? __dirname;
for (const lang of ['zh-CN', 'en']) {
  const svgPath = path.join(docsDir, `smart-runtime.${lang}.svg`);
  fs.writeFileSync(svgPath, build(TEXT[lang]));
  console.log(lang, '→', svgPath, fs.statSync(svgPath).size, 'B');
}
