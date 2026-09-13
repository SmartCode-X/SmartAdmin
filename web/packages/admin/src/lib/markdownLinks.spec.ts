import { describe, it, expect, vi } from 'vitest'
import { classifyMarkdownLink, handleMarkdownLinkClick } from './markdownLinks'

describe('classifyMarkdownLink', () => {
  it('单个 / 开头的站内路径走路由', () => {
    expect(classifyMarkdownLink('/system/notice')).toEqual({ kind: 'route', to: '/system/notice' })
    expect(classifyMarkdownLink(' /a?b=1#c ')).toEqual({ kind: 'route', to: '/a?b=1#c' })
  })
  it('http(s) 绝对地址是外链', () => {
    expect(classifyMarkdownLink('https://example.com/x')).toEqual({
      kind: 'external',
      href: 'https://example.com/x',
    })
  })
  it('协议相对 URL、锚点、mailto、空值都不接管', () => {
    expect(classifyMarkdownLink('//evil.com')).toEqual({ kind: 'native' })
    expect(classifyMarkdownLink('/\\evil.com')).toEqual({ kind: 'native' })
    expect(classifyMarkdownLink('#top')).toEqual({ kind: 'native' })
    expect(classifyMarkdownLink('mailto:a@b.c')).toEqual({ kind: 'native' })
    expect(classifyMarkdownLink('')).toEqual({ kind: 'native' })
    expect(classifyMarkdownLink(null)).toEqual({ kind: 'native' })
  })
})

// 委托:在容器上装一次 click,正文里的 <a> 逐个命中。happy-dom 提供真实的事件冒泡与 closest()。
function mount(html: string) {
  const root = document.createElement('div')
  root.innerHTML = html
  document.body.appendChild(root)
  const push = vi.fn()
  const handled: boolean[] = []
  root.addEventListener('click', ev =>
    handled.push(handleMarkdownLinkClick(ev as MouseEvent, push)),
  )
  return { root, push, handled }
}
function click(el: Element, init: MouseEventInit = {}) {
  const ev = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0, ...init })
  el.dispatchEvent(ev)
  return ev
}

describe('handleMarkdownLinkClick', () => {
  it('站内链接:阻止默认跳转,改走 push(点在 <a> 的子元素上也算)', () => {
    const { root, push, handled } = mount('<p><a href="/system/notice"><b>去</b></a></p>')
    const ev = click(root.querySelector('b')!)
    expect(ev.defaultPrevented).toBe(true)
    expect(push).toHaveBeenCalledWith('/system/notice')
    expect(handled).toEqual([true])
  })

  it('Ctrl / Cmd + 点击:不接管,交给浏览器新开标签', () => {
    const { root, push } = mount('<a href="/system/notice">x</a>')
    const a = root.querySelector('a')!
    expect(click(a, { ctrlKey: true }).defaultPrevented).toBe(false)
    expect(click(a, { metaKey: true }).defaultPrevented).toBe(false)
    expect(push).not.toHaveBeenCalled()
  })

  it('外链:阻止默认,window.open 新标签且 noopener', () => {
    const open = vi.spyOn(window, 'open').mockImplementation(() => null)
    const { root, push } = mount('<a href="https://example.com/doc">x</a>')
    const ev = click(root.querySelector('a')!)
    expect(ev.defaultPrevented).toBe(true)
    expect(open).toHaveBeenCalledWith('https://example.com/doc', '_blank', 'noopener')
    expect(push).not.toHaveBeenCalled()
  })

  it('协议相对 URL 与非链接元素:原样放行', () => {
    const { root, push, handled } = mount('<a href="//evil.com">x</a><span>plain</span>')
    expect(click(root.querySelector('a')!).defaultPrevented).toBe(false)
    expect(click(root.querySelector('span')!).defaultPrevented).toBe(false)
    expect(push).not.toHaveBeenCalled()
    expect(handled).toEqual([false, false])
  })
})
