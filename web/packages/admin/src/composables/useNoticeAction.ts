// 通知动作按钮的执行样板:确认 / 意见输入 → 请求接口或跳转页面 → toast。
// 权限仍由目标端点自己判(403 照常提示),这里不做任何鉴权推断。仅限 setup 中调用(useDialog/useMessage)。
import { h, ref } from 'vue'
import { NInput, useDialog, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { callApi } from '#/api'
import { classifyNoticeAction, noticeActionLabel } from '#/lib/noticeActions'
import type { NoticeAction } from '#/types/api'
import { translateError } from '#/utils/error'

/** 动作样式 → Naive 按钮 type;未知样式按 default。 */
export function noticeActionButtonType(a: NoticeAction): 'primary' | 'error' | 'default' {
  return a.style === 'primary' || a.style === 'error' ? a.style : 'default'
}

export function useNoticeAction() {
  const { t, te } = useI18n()
  const dialog = useDialog()
  const message = useMessage()
  const router = useRouter()

  const label = (a: NoticeAction) => noticeActionLabel(a.label, t, te)
  const buttonType = noticeActionButtonType

  /** 确认框;resolve(是否确认)。 */
  function confirm(a: NoticeAction): Promise<boolean> {
    return new Promise(resolve => {
      dialog.create({
        type: a.style === 'error' ? 'error' : 'warning',
        title: t('common.confirmTitle'),
        content: t('notice.actionConfirm', { label: label(a) }),
        positiveText: t('common.confirm'),
        negativeText: t('common.cancel'),
        onPositiveClick: () => resolve(true),
        onNegativeClick: () => resolve(false),
        onClose: () => resolve(false),
        onMaskClick: () => resolve(false),
        onEsc: () => resolve(false),
      })
    })
  }

  /** 意见输入框;resolve(意见文本),取消 resolve(undefined)。 */
  function askComment(a: NoticeAction): Promise<string | undefined> {
    const text = ref('')
    return new Promise(resolve => {
      dialog.create({
        type: 'info',
        title: label(a),
        content: () =>
          h(NInput, {
            type: 'textarea',
            value: text.value,
            placeholder: t('notice.actionCommentPlaceholder'),
            autosize: { minRows: 3, maxRows: 6 },
            maxlength: 500,
            showCount: true,
            onUpdateValue: (v: string) => {
              text.value = v
            },
          }),
        positiveText: t('common.confirm'),
        negativeText: t('common.cancel'),
        onPositiveClick: () => resolve(text.value.trim()),
        onNegativeClick: () => resolve(undefined),
        onClose: () => resolve(undefined),
        onMaskClick: () => resolve(undefined),
        onEsc: () => resolve(undefined),
      })
    })
  }

  /**
   * 执行一个动作。resolve(true) 仅当接口成功或已跳转;取消、失败都是 false。
   * url 以 /api/ 开头按 method 请求;其它站内路径 router.push(method 忽略)。
   */
  async function run(a: NoticeAction): Promise<boolean> {
    const kind = classifyNoticeAction(a)
    if (kind === 'invalid') {
      message.error(t('notice.actionInvalidUrl'))
      return false
    }
    if (kind === 'route') {
      await router.push(a.url)
      return true
    }
    let comment: string | undefined
    if (a.comment) {
      comment = await askComment(a)
      if (comment === undefined) return false
    } else if (a.confirm && !(await confirm(a))) {
      return false
    }
    try {
      await callApi(a.method, a.url, comment === undefined ? undefined : { comment })
      message.success(t('notice.actionDone'))
      return true
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  return { run, label, buttonType }
}
