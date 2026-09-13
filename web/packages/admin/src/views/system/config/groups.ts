// 分类配置中心的分组归属:某分组由某结构化 Tab 管理,该组的行都显示在那个 Tab 里
// (Tab 认领的键渲染成字段,其余在「本组其它配置」);「其他配置」只列不归任何结构化 Tab 的分组。
import { ref } from 'vue'

/** 结构化分组 → 管它的 Tab(值是 config.tab.* 的 i18n 子键)。 */
export const STRUCTURED_GROUP_TABS: Readonly<Record<string, string>> = {
  sys: 'base',
  security: 'security',
  externalauth: 'externalAuth',
  upload: 'upload',
  job: 'job',
}

export const STRUCTURED_GROUPS = Object.keys(STRUCTURED_GROUP_TABS)

/**
 * 配置行增删改的版本号。各 Tab 以 show:lazy 常驻,切回来不会重新挂载;
 * 行被改动后 bump 一次,「本组其它配置」据此重拉,不用刷新整页。
 */
export const configRevision = ref(0)

export function bumpConfigRevision() {
  configRevision.value++
}
