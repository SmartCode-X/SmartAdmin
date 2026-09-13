import { describe, expect, it } from 'vitest'
import { joinPermission, splitPermission } from './menu'

// 按钮 permission 字段的拆合:多选框里编辑的是数组,库里存的是 ; 连接的一条串,两边必须可逆且去重。
describe('splitPermission / joinPermission', () => {
  it('拆开时去空白、去空项、去重,保持顺序', () => {
    expect(
      splitPermission(
        ' GET:/api/v1/sys/user/page ; GET:/api/v1/sys/user/{id};; GET:/api/v1/sys/user/page ',
      ),
    ).toEqual(['GET:/api/v1/sys/user/page', 'GET:/api/v1/sys/user/{id}'])
  })

  it('空值拆成空数组', () => {
    expect(splitPermission('')).toEqual([])
    expect(splitPermission(null)).toEqual([])
    expect(splitPermission(undefined)).toEqual([])
    expect(splitPermission(' ; ')).toEqual([])
  })

  it('拼回去与拆开互逆', () => {
    const stored = 'GET:/api/v1/sys/user/page;GET:/api/v1/sys/user/{id}'
    expect(joinPermission(splitPermission(stored))).toBe(stored)
    expect(joinPermission([])).toBe('')
    expect(joinPermission([' GET:/api/v1/ping ', ''])).toBe('GET:/api/v1/ping')
  })
})
