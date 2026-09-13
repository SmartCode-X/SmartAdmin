import { describe, it, expect } from 'vitest'
import { MenuType, type MenuTreeNode } from '#/types/menu'
import { buildGroups, collectChecked, recomputeGroup } from './grantMenuGroups'

const node = (
  id: number,
  type: MenuType,
  title: string,
  children: MenuTreeNode[] = [],
  extra: Partial<MenuTreeNode> = {},
): MenuTreeNode => ({
  id,
  parentId: 0,
  type,
  title,
  permission: '',
  sort: 0,
  enabled: true,
  visible: true,
  children,
  ...extra,
})

const ANCHOR = '接口权限(无页面)'

// 系统运维目录:一个页面(带两个按钮)+ 一个直挂目录的权限锚点(ping) —— 种子里就是这个形状
const tree: MenuTreeNode[] = [
  node(300, MenuType.Catalog, '系统运维', [
    node(350, MenuType.Menu, '消息通知', [
      node(351, MenuType.Button, '通知-查询'),
      node(352, MenuType.Button, '通知-发布'),
    ]),
    node(301, MenuType.Button, '连通性探针', [], { permission: 'GET:/api/v1/ping' }),
  ]),
  node(100, MenuType.Menu, '工作台'),
]

describe('buildGroups', () => {
  it('目录直属按钮渲染成一行 anchor 行', () => {
    const [ops] = buildGroups(tree, new Set(), ANCHOR)
    expect(ops.menus.map(m => m.title)).toEqual([ANCHOR, '消息通知'])
    const anchor = ops.menus[0]
    expect(anchor.anchor).toBe(true)
    expect(anchor.id).toBe(300)
    expect(anchor.buttons.map(b => b.id)).toEqual([301])
  })

  it('顶级页面自成一组;根级直挂按钮并进合成组', () => {
    const groups = buildGroups(
      [...tree, node(7, MenuType.Button, '根级锚点')],
      new Set([7]),
      ANCHOR,
    )
    expect(groups.map(g => g.title)).toEqual(['系统运维', '工作台', ANCHOR])
    const synthetic = groups[2]
    expect(synthetic.synthetic).toBe(true)
    expect(synthetic.checked).toBe(true)
  })

  it('嵌套目录的页面与锚点都能走到,标题带父目录前缀', () => {
    const nested: MenuTreeNode[] = [
      node(1, MenuType.Catalog, '外层', [
        node(2, MenuType.Catalog, '内层', [
          node(3, MenuType.Menu, '页面'),
          node(4, MenuType.Button, '内层锚点'),
        ]),
      ]),
    ]
    const [g] = buildGroups(nested, new Set(), ANCHOR)
    expect(g.menus.map(m => m.title)).toEqual([`内层 / ${ANCHOR}`, '内层 / 页面'])
  })

  it('已授权的锚点按钮回显为勾选;目录半勾', () => {
    const [ops] = buildGroups(tree, new Set([301]), ANCHOR)
    expect(ops.menus[0].checked).toBe(true)
    expect(ops.indeterminate).toBe(true)
    expect(ops.checked).toBe(false)
  })
})

describe('collectChecked', () => {
  it('anchor 行与合成组自身不进结果,按钮 id 照常提交', () => {
    const groups = buildGroups(
      [...tree, node(7, MenuType.Button, '根级锚点')],
      new Set([301, 7]),
      ANCHOR,
    )
    expect(collectChecked(groups).toSorted((a, b) => a - b)).toEqual([7, 300, 301])
  })

  it('全勾一个目录:目录、页面、按钮、锚点按钮一起提交', () => {
    const groups = buildGroups(tree, new Set(), ANCHOR)
    const ops = groups[0]
    for (const m of ops.menus) {
      m.checked = true
      for (const b of m.buttons) b.checked = true
    }
    recomputeGroup(ops)
    expect(ops.checked).toBe(true)
    expect(collectChecked(groups).toSorted((a, b) => a - b)).toEqual([300, 301, 350, 351, 352])
  })
})
