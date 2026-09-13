<script setup lang="ts">
// 角色授权表格:目录 | 页面 | 按钮 三列。分组逻辑在 grantMenuGroups.ts(纯函数,有单测),这里只管渲染与勾选联动。
// 直接挂在目录下的按钮(无页面权限项,如只给移动端 / 第三方调的接口)渲染成该目录组里的一行,
// 页面列显示「接口权限(无页面)」;勾选提交时只提交按钮 id,不需要为它们建假页面。
import { computed, reactive, ref, watch } from 'vue'
import { NButton, NCheckbox, NInput, NSelect, NTag } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '#/components/AppIcon.vue'
import type { MenuTreeNode } from '#/types/menu'
import type { ModuleRow } from '#/types/api'
import {
  buildGroups,
  collectChecked as collect,
  recomputeGroup,
  type CatalogGroup,
  type MenuRow,
} from './grantMenuGroups'

const UNASSIGNED = 0

const props = defineProps<{
  tree: MenuTreeNode[]
  granted: number[]
  modules: ModuleRow[]
  defaultModuleId: number
}>()

const emit = defineEmits<{ (e: 'update:checked', ids: number[]): void }>()

const { t } = useI18n()
const search = ref('')
const moduleId = ref(props.defaultModuleId)
watch(
  () => props.defaultModuleId,
  v => {
    moduleId.value = v
  },
)

const moduleOptions = computed(() => [
  ...props.modules.map(m => ({ label: m.title, value: m.id })),
  { label: t('menu.moduleUnassigned'), value: UNASSIGNED },
])

const allGroups = reactive<CatalogGroup[]>([])

watch(
  () => [props.tree, props.granted] as const,
  ([tree, granted]) => {
    const groups = buildGroups(tree, new Set(granted), t('role.apiOnlyRow'))
    allGroups.splice(0, allGroups.length, ...groups)
  },
  { immediate: true },
)

const filteredByModule = computed(() =>
  allGroups.filter(g => {
    const node = props.tree.find(n => n.id === g.id)
    return moduleId.value === UNASSIGNED
      ? node?.moduleId == null
      : node?.moduleId === moduleId.value
  }),
)

const filteredGroups = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return filteredByModule.value
  return filteredByModule.value
    .map(g => {
      if (g.title.toLowerCase().includes(q)) return g
      const menus = g.menus.filter(
        m =>
          m.title.toLowerCase().includes(q) ||
          m.buttons.some(b => b.title.toLowerCase().includes(q)),
      )
      return menus.length ? { ...g, menus } : null
    })
    .filter(Boolean) as CatalogGroup[]
})

function toggleCatalog(group: CatalogGroup, val: boolean) {
  group.checked = val
  group.indeterminate = false
  group.menus.forEach(m => {
    m.checked = val
    m.buttons.forEach(b => {
      b.checked = val
    })
  })
  emitChecked()
}

function toggleMenu(group: CatalogGroup, menu: MenuRow, val: boolean) {
  menu.checked = val
  menu.buttons.forEach(b => {
    b.checked = val
  })
  recomputeGroup(group)
  emitChecked()
}

function toggleButton(group: CatalogGroup, menu: MenuRow) {
  const allChecked = menu.buttons.length > 0 && menu.buttons.every(b => b.checked)
  // 页面行:按钮全勾时顺手把页面勾上;anchor 行自身不是节点,勾选态就等于"按钮是否全勾"
  if (allChecked || menu.anchor) menu.checked = allChecked
  recomputeGroup(group)
  emitChecked()
}

function collectChecked(): number[] {
  return collect(allGroups)
}

function emitChecked() {
  emit('update:checked', collectChecked())
}

// ── 折叠/展开 ──
const collapsed = reactive(new Set<number>())
const allCollapsed = computed(
  () => filteredGroups.value.length > 0 && filteredGroups.value.every(g => collapsed.has(g.id)),
)

function toggleCollapse(id: number) {
  if (collapsed.has(id)) collapsed.delete(id)
  else collapsed.add(id)
}
function toggleCollapseAll() {
  if (allCollapsed.value) {
    collapsed.clear()
  } else {
    for (const g of filteredGroups.value) collapsed.add(g.id)
  }
}

defineExpose({ collectChecked })
</script>

<template>
  <div class="grant-menu-table">
    <div class="grant-toolbar">
      <n-select
        v-model:value="moduleId"
        :options="moduleOptions"
        :placeholder="t('menu.module')"
        size="small"
        style="width: 180px"
      />
      <n-input
        v-model:value="search"
        :placeholder="t('common.search')"
        clearable
        size="small"
        style="flex: 1"
      />
      <n-button size="small" quaternary @click="toggleCollapseAll">
        <template #icon>
          <AppIcon
            :icon="allCollapsed ? 'ph:arrows-out-line-vertical' : 'ph:arrows-in-line-vertical'"
            :size="16"
          />
        </template>
        {{ allCollapsed ? t('common.expandAll') : t('common.collapseAll') }}
      </n-button>
    </div>
    <div class="grant-grid">
      <div class="grant-header">
        <div class="col-catalog">{{ t('role.catalog') }}</div>
        <div class="col-menu">{{ t('role.menuCol') }}</div>
        <div class="col-buttons">{{ t('role.buttonsCol') }}</div>
      </div>
      <template v-for="group in filteredGroups" :key="group.id">
        <div class="grant-group" :style="{ '--rows': group.menus.length }">
          <div class="col-catalog" @click="toggleCollapse(group.id)">
            <span class="collapse-arrow" :class="{ 'is-collapsed': collapsed.has(group.id) }">
              &#9662;
            </span>
            <n-checkbox
              :checked="group.checked"
              :indeterminate="group.indeterminate"
              @click.stop
              @update:checked="toggleCatalog(group, $event)"
            >
              {{ group.title }}
            </n-checkbox>
          </div>
          <div v-show="!collapsed.has(group.id)" class="group-rows">
            <div
              v-for="menu in group.menus"
              :key="menu.anchor ? `anchor-${menu.id}` : menu.id"
              class="grant-row"
            >
              <div class="col-menu">
                <n-checkbox
                  :checked="menu.checked"
                  @update:checked="toggleMenu(group, menu, $event)"
                >
                  <n-tag v-if="menu.anchor" size="tiny" :bordered="false" class="anchor-tag">
                    {{ menu.title }}
                  </n-tag>
                  <template v-else>{{ menu.title }}</template>
                </n-checkbox>
              </div>
              <div class="col-buttons">
                <n-checkbox
                  v-for="btn in menu.buttons"
                  :key="btn.id"
                  :checked="btn.checked"
                  @update:checked="
                    (v: boolean) => {
                      btn.checked = v
                      toggleButton(group, menu)
                    }
                  "
                >
                  {{ btn.title }}
                </n-checkbox>
                <span v-if="!menu.buttons.length" class="no-buttons">—</span>
              </div>
            </div>
          </div>
        </div>
      </template>
      <div v-if="filteredGroups.length === 0" class="grant-empty">—</div>
    </div>
  </div>
</template>

<style scoped>
.grant-menu-table {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.grant-toolbar {
  display: flex;
  gap: 8px;
}
.grant-grid {
  border: 1px solid var(--n-border-color, #e0e0e6);
  border-radius: 4px;
  font-size: 13px;
  overflow: hidden;
}
.grant-header {
  display: flex;
  background: var(--n-th-color, #fafafc);
  font-weight: 600;
  font-size: 12px;
  border-bottom: 1px solid var(--n-border-color, #e0e0e6);
}
.grant-header > div {
  padding: 8px 12px;
}
.grant-group {
  display: flex;
  border-bottom: 1px solid var(--n-border-color, #e0e0e6);
}
.grant-group:last-child {
  border-bottom: none;
}
.grant-group > .col-catalog {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 8px 12px;
  border-right: 1px solid var(--n-border-color, #e0e0e6);
  cursor: pointer;
  user-select: none;
}
.collapse-arrow {
  display: inline-block;
  font-size: 10px;
  transition: transform 0.2s;
  color: var(--n-text-color-disabled, #999);
}
.collapse-arrow.is-collapsed {
  transform: rotate(-90deg);
}
.group-rows {
  flex: 1;
  min-width: 0;
}
.grant-row {
  display: flex;
  border-bottom: 1px solid var(--n-border-color, #e0e0e6);
}
.grant-row:last-child {
  border-bottom: none;
}
.col-catalog {
  width: 130px;
  flex-shrink: 0;
}
.col-menu {
  width: 140px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  padding: 6px 12px;
  border-right: 1px solid var(--n-border-color, #e0e0e6);
}
.anchor-tag {
  font-size: 12px;
}
.col-buttons {
  flex: 1;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 12px;
  padding: 6px 12px;
  min-width: 0;
}
.no-buttons {
  color: var(--n-text-color-disabled, #c0c0c0);
}
.grant-empty {
  text-align: center;
  padding: 24px;
  color: var(--n-text-color-disabled, #c0c0c0);
}
</style>
