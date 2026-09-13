// 角色授权表格的分组逻辑(纯函数,GrantMenuTable.vue 只管渲染与勾选联动)。
// 三列:目录 | 页面 | 按钮。按钮正常挂在页面下;直接挂在目录下的按钮是"无页面权限项"
//(只给移动端 / 第三方调的接口,没有对应页面),渲染成该目录组里的一行 anchor 行,否则它们勾不到。
import { MenuType, type MenuTreeNode } from '#/types/menu'

export interface ButtonItem {
  id: number
  title: string
  checked: boolean
}
export interface MenuRow {
  id: number
  title: string
  checked: boolean
  buttons: ButtonItem[]
  /** 无页面权限项行:承载目录直属按钮。id 是所属目录的 id,只作 key,不进 collectChecked。 */
  anchor?: boolean
}
export interface CatalogGroup {
  id: number
  title: string
  checked: boolean
  indeterminate: boolean
  menus: MenuRow[]
  /** 根级直挂按钮的合成分组(id=0),自身不是可授权节点,不进 collectChecked。 */
  synthetic?: boolean
}

function toButtons(nodes: MenuTreeNode[], grantedSet: Set<number>): ButtonItem[] {
  return nodes
    .filter(b => b.type === MenuType.Button)
    .map(b => ({ id: b.id, title: b.title, checked: grantedSet.has(b.id) }))
}

function toMenuRow(m: MenuTreeNode, grantedSet: Set<number>, prefix = ''): MenuRow {
  return {
    id: m.id,
    title: prefix + m.title,
    checked: grantedSet.has(m.id),
    buttons: toButtons(m.children, grantedSet),
  }
}

function anchorRow(
  ownerId: number,
  buttons: ButtonItem[],
  anchorTitle: string,
  prefix = '',
): MenuRow {
  return {
    id: ownerId,
    title: prefix + anchorTitle,
    checked: buttons.length > 0 && buttons.every(b => b.checked),
    buttons,
    anchor: true,
  }
}

/**
 * 一个目录的全部行:直属按钮 → anchor 行(有才出);直属页面 → 各一行;嵌套目录 → 递归,标题带上父目录前缀。
 */
function catalogRows(
  node: MenuTreeNode,
  grantedSet: Set<number>,
  anchorTitle: string,
  prefix = '',
): MenuRow[] {
  const rows: MenuRow[] = []
  const buttons = toButtons(node.children, grantedSet)
  if (buttons.length) rows.push(anchorRow(node.id, buttons, anchorTitle, prefix))
  for (const child of node.children) {
    if (child.type === MenuType.Menu) rows.push(toMenuRow(child, grantedSet, prefix))
    else if (child.type === MenuType.Catalog)
      rows.push(...catalogRows(child, grantedSet, anchorTitle, `${prefix}${child.title} / `))
  }
  return rows
}

export function recomputeGroup(group: CatalogGroup) {
  // anchor 行自身不是节点,只数它的按钮
  const items = group.menus.flatMap(m => (m.anchor ? m.buttons : [m, ...m.buttons]))
  const checkedCount = items.filter(x => x.checked).length
  group.checked = checkedCount > 0 && checkedCount === items.length
  group.indeterminate = checkedCount > 0 && checkedCount < items.length
}

/**
 * 顶层节点 → 分组。顶级页面(如工作台)自成一组;顶级目录一组;根级直挂按钮并进一个合成组。
 * @param anchorTitle 无页面权限项行的显示文案(i18n 由调用方给)
 */
export function buildGroups(
  tree: MenuTreeNode[],
  grantedSet: Set<number>,
  anchorTitle: string,
): CatalogGroup[] {
  const groups: CatalogGroup[] = []
  for (const node of tree) {
    if (node.type === MenuType.Button) continue
    const menus =
      node.type === MenuType.Menu
        ? [toMenuRow(node, grantedSet)]
        : catalogRows(node, grantedSet, anchorTitle)
    const group: CatalogGroup = {
      id: node.id,
      title: node.title,
      checked: grantedSet.has(node.id),
      indeterminate: false,
      menus,
    }
    recomputeGroup(group)
    groups.push(group)
  }
  const rootButtons = toButtons(tree, grantedSet)
  if (rootButtons.length) {
    const group: CatalogGroup = {
      id: 0,
      title: anchorTitle,
      checked: false,
      indeterminate: false,
      menus: [anchorRow(0, rootButtons, anchorTitle)],
      synthetic: true,
    }
    recomputeGroup(group)
    groups.push(group)
  }
  return groups
}

/** 勾选态 → 要提交的菜单 id:目录(全勾或半勾)、页面、按钮;anchor 行与合成组自身不算节点。 */
export function collectChecked(groups: CatalogGroup[]): number[] {
  const ids: number[] = []
  for (const g of groups) {
    if (!g.synthetic && (g.checked || g.indeterminate)) ids.push(g.id)
    for (const m of g.menus) {
      if (!m.anchor && m.checked) ids.push(m.id)
      for (const b of m.buttons) if (b.checked) ids.push(b.id)
    }
  }
  return ids
}
