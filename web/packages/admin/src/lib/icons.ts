// SmartAdmin 侧图标 bootstrap:把 4 套离线集 + 本地 SVG 注册进 smart-naive-icon,并同步装入 ph 子集。
// 选择器/渲染器逻辑在该独立 npm 包里(见 components/IconPicker、components/AppIcon.vue),这里只做「注册配置」。
// SmartAdmin 只保留三个薄封装(本文件 + AppIcon.vue + IconPicker/index.vue)。组件本体的 bug 去组件包的仓库提 issue
// 或本地 patch,不要把组件逻辑复制进 SmartAdmin 修——那样同一套逻辑就有了两份。
import { addCollection } from '@iconify/vue'
import { setupSmartIcon, type IconCollection, type IconifyJSON } from 'smart-naive-icon'
import phSubset from '#/assets/icons/ph-subset.json'

// 增删默认离线集:装/卸 `@iconify-json/<prefix>`,并在此加/删一行同 prefix 的 loader。
// 每套是独立懒加载 chunk;`ph` 是全站默认集,整集只在选择器打开、或渲染到子集外的图标时才拉。
const collections: IconCollection[] = [
  {
    prefix: 'ph',
    name: 'Phosphor',
    loader: () => import('@iconify-json/ph/icons.json').then(m => m.default as IconifyJSON),
  },
  {
    prefix: 'lucide',
    name: 'Lucide',
    loader: () => import('@iconify-json/lucide/icons.json').then(m => m.default as IconifyJSON),
  },
  {
    prefix: 'ep',
    name: 'Element Plus',
    loader: () => import('@iconify-json/ep/icons.json').then(m => m.default as IconifyJSON),
  },
  {
    prefix: 'ant-design',
    name: 'Ant Design',
    loader: () => import('@iconify-json/ant-design/icons.json').then(m => m.default as IconifyJSON),
  },
]

// 包没有「不预热」的开关:preloadIcons 拿不到 prefix 就退回第一套,故给一个必不存在的前缀把预热顶掉——
// 只同步注册这份子集:预热整套 9000+ 图标(4.5 MB / 946 KB gz)会让首屏白拉掉 dist 四成的体积。
const NO_PRELOAD = '__none__'

// 内核自带的本地 SVG(src/assets/svg/*.svg);消费方的经 createSmartAdmin({ icons }) 以同样的 glob 形状传入。
const kernelIcons = import.meta.glob('../assets/svg/*.svg', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

/**
 * 首屏调用一次:同步装入 ph 子集(内核的 + 消费方经 iconSets 传入的),再注册离线集 + 本地 SVG(内核自带 + 消费方传入)。
 */
export function setupIcons(
  extraIcons: Record<string, string> = {},
  extraSets: IconifyJSON[] = [],
): void {
  // 子集同步入库,首帧即可离线渲染静态图标,不必等异步预热,也不会命中外部 CDN。
  // 内核子集由 `npm run gen:icons` 从包的 src 与种子菜单图标生成(scripts/gen-icon-subset.mjs);
  // 消费方的由 smart-admin-icons 从它自己的 src 生成。两份有重名也无妨,同名图标数据相同,后注册的覆盖前者。
  addCollection(phSubset as IconifyJSON)
  for (const set of extraSets) addCollection(set)
  setupSmartIcon({
    collections,
    localIcons: { ...kernelIcons, ...extraIcons },
    preloadPrefix: NO_PRELOAD,
  })
}
