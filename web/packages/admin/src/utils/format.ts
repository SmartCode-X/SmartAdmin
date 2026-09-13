// 全站共用的展示格式化,统一到这里 —— 时间截断、字节格式化这类逻辑散在各视图里各自实现,
// 容易抄出不一致的口径(如文件页 1 位小数、监控页 2 位),同一个数在两页显示会不一样。

/**
 * 后端时间串 → 展示文本。
 *
 * 后端下发的是**服务器本地时区**的 ISO 串(无 Z),所以只做截断,不走 `new Date()` ——
 * 一旦交给 Date 解析再本地化,浏览器会把它当 UTC,时区不同的客户端整体偏移几小时。
 *
 * @param seconds 到秒(默认)还是到分。
 * @param empty 空值占位,列表里通常给 '—',内联文本给 ''。
 */
export function fmtDateTime(
  value?: string | null,
  { seconds = true, empty = '' }: { seconds?: boolean; empty?: string } = {},
): string {
  const s = (value ?? '').trim()
  if (!s) return empty
  return s.slice(0, seconds ? 19 : 16).replace('T', ' ')
}

const BYTE_UNITS = ['B', 'KB', 'MB', 'GB', 'TB'] as const

/** 字节 → 人类可读(二进制单位)。B 不带小数,其余两位。 */
export function fmtBytes(n?: number | null): string {
  if (!n || n <= 0) return '0 B'
  const i = Math.min(Math.floor(Math.log(n) / Math.log(1024)), BYTE_UNITS.length - 1)
  return `${(n / 1024 ** i).toFixed(i === 0 ? 0 : 2)} ${BYTE_UNITS[i]}`
}

/** 日志类记录的操作人:姓名优先,只留下 Id 时退回 Id(日志只存 Id,姓名是读取时回填的)。 */
export function operatorText(
  row: { operatorName?: string | null; operatorId?: number | null },
  empty = '—',
): string {
  return row.operatorName || (row.operatorId != null ? String(row.operatorId) : empty)
}
