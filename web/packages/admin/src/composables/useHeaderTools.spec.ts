import { describe, expect, it } from 'vitest'
import { h } from 'vue'
import { registerHeaderTool, useHeaderTools } from './useHeaderTools'

describe('registerHeaderTool', () => {
  it('按 order 升序,同 order 保持登记先后', () => {
    const tools = useHeaderTools()
    const offB = registerHeaderTool({ key: 'b', icon: 'ph:b', label: 'B', onClick: () => {} })
    const offC = registerHeaderTool({ key: 'c', icon: 'ph:c', label: 'C', onClick: () => {} })
    const offA = registerHeaderTool({
      key: 'a',
      icon: 'ph:a',
      label: 'A',
      onClick: () => {},
      order: -1,
    })

    expect(tools.value.map(t => t.key)).toEqual(['a', 'b', 'c'])

    offA()
    offB()
    offC()
    expect(tools.value).toEqual([])
  })

  it('组件模式登记,重复 key 以最后一次为准且旧注销函数不误删', () => {
    const tools = useHeaderTools()
    const Todo = { render: () => h('div') }
    const offOld = registerHeaderTool({ key: 'todo', component: Todo })
    const offNew = registerHeaderTool({
      key: 'todo',
      icon: 'ph:check',
      label: '待办',
      onClick: () => {},
    })

    offOld()
    expect(tools.value.map(t => t.key)).toEqual(['todo'])
    expect(tools.value[0].label).toBe('待办')

    offNew()
    expect(tools.value).toEqual([])
  })
})
