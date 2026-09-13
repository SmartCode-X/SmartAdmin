// 6 主色候选(design_handoff §可配置项)。默认 = 第一个青绿 #14B8A6;
// 靛蓝 #646CFF 保留为可选手项(品牌 Logo 固定品牌色、不随用户换的 accent 变)。
export const ACCENTS = ['#14B8A6', '#646CFF', '#7C5CFF', '#0EA5E9', '#EC4899', '#F97316'] as const

export type Accent = (typeof ACCENTS)[number]
