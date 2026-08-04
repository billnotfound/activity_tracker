<!--
  Tags view — tag rules editor with inline editing
-->
<template>
  <div class="tags-page">
    <h2 class="page-title">{{ t('tags.pageTitle') }}</h2>

    <!-- Alerts -->
    <div v-if="tagError" class="alert-banner error mb-3">
      <strong>{{ t('tags.tagsError') }}</strong> {{ tagError }}
    </div>
    <div v-if="savedMsg" class="alert-banner success mb-3">
      {{ savedMsg }}
      <button class="close-btn" @click="savedMsg = ''" :aria-label="t('common.close')"><X :size="16" /></button>
    </div>
    <div v-if="saveError" class="alert-banner error mb-3">
      {{ saveError }}
      <button class="close-btn" @click="saveError = ''" :aria-label="t('common.close')"><X :size="16" /></button>
    </div>

    <!-- Rules table -->
    <MemphisCard class="rules-card">
      <div class="card-header-row">
        <h3 class="card-title">
          {{ t('tags.card.ruleList', { count: rules.length }) }}
          <small v-if="newCount" class="unsaved-count">
            {{ t('tags.unsavedCount', { count: newCount }) }}
          </small>
        </h3>
        <div class="actions">
          <button class="icon-btn secondary" @click="addRule" :title="t('tags.addRule')" :aria-label="t('tags.addRule')">
            <Plus :size="18" />
          </button>
          <button
            class="icon-btn primary"
            @click="saveRules"
            :disabled="saving || (!newCount && !tagsDirty)"
            :title="saving ? t('common.saving') : t('common.save')"
            :aria-label="t('common.save')"
          >
            <Save :size="18" :class="{ spin: saving }" />
          </button>
        </div>
      </div>

      <DataTable
        :value="rules"
        class="memphis-datatable"
        stripedRows
        :rowClass="rowClass"
      >
        <Column field="tag" :header="t('tags.column.tag')" style="min-width: 120px">
          <template #body="{ data }">
            <input
              v-model="data.tag"
              class="memphis-input compact"
              :placeholder="t('tags.placeholder.tag')"
              @input="markDirty(data)"
            />
          </template>
        </Column>
        <Column field="process" :header="t('tags.column.process')" style="min-width: 150px">
          <template #body="{ data }">
            <input
              v-model="data.process"
              class="memphis-input compact"
              :placeholder="t('tags.placeholder.any')"
              @input="markDirty(data)"
            />
          </template>
        </Column>
        <Column field="titlePattern" :header="t('tags.column.titlePattern')" style="min-width: 180px">
          <template #body="{ data }">
            <input
              v-model="data.titlePattern"
              class="memphis-input compact"
              :placeholder="t('tags.placeholder.any')"
              @input="markDirty(data)"
            />
          </template>
        </Column>
        <Column field="weight" :header="t('tags.column.weight')" style="width: 100px">
          <template #body="{ data }">
            <input
              v-model.number="data.weight"
              type="number"
              class="memphis-input compact"
              @input="markDirty(data)"
            />
          </template>
        </Column>
        <Column field="mode" :header="t('tags.column.mode')" style="width: 120px">
          <template #body="{ data }">
            <select
              v-model="data.mode"
              class="memphis-select compact"
              @change="markDirty(data)"
            >
              <option v-for="m in modes" :key="m.v" :value="m.v">
                {{ m.label }}
              </option>
            </select>
          </template>
        </Column>
        <Column style="width: 60px">
          <template #body="{ index }">
            <button
              class="icon-btn danger"
              @click="removeRule(index)"
              :title="t('common.delete')"
              :aria-label="t('common.delete')"
            >
              <Trash2 :size="18" />
            </button>
          </template>
        </Column>
        <template #empty>
          <div class="empty-state">{{ t('tags.noRules') }}</div>
        </template>
      </DataTable>
    </MemphisCard>

    <!-- Title rules card -->
    <MemphisCard class="rules-card title-rules-card">
      <div class="card-header-row">
        <h3 class="card-title">
          {{ t('tags.card.titleRuleList', { count: titleRules.length }) }}
          <small v-if="newTitleCount" class="unsaved-count">
            {{ t('tags.unsavedCount', { count: newTitleCount }) }}
          </small>
        </h3>
        <div class="actions">
          <button class="icon-btn secondary" @click="addTitleRule" :title="t('tags.titleRule.addRule')" :aria-label="t('tags.titleRule.addRule')">
            <Plus :size="18" />
          </button>
          <button
            class="icon-btn primary"
            @click="saveTitleRules"
            :disabled="titleSaving || (!newTitleCount && !titleDirty)"
            :title="titleSaving ? t('common.saving') : t('common.save')"
            :aria-label="t('common.save')"
          >
            <Save :size="18" :class="{ spin: titleSaving }" />
          </button>
          <button
            class="icon-btn secondary"
            @click="normalizeDb"
            :disabled="titleNormalizing"
            :title="titleNormalizing ? t('common.saving') : t('tags.titleRule.normalizeDb')"
            :aria-label="t('tags.titleRule.normalizeDb')"
          >
            <History :size="18" :class="{ spin: titleNormalizing }" />
          </button>
        </div>
      </div>

      <div v-if="titleRulesError" class="alert-banner error mb-3">
        <strong>{{ t('tags.titleRulesError') }}</strong> {{ titleRulesError }}
      </div>
      <div v-if="titleSavedMsg || titleNormalizeMsg" class="alert-banner success mb-3">
        {{ titleSavedMsg || titleNormalizeMsg }}
        <button class="close-btn" @click="titleSavedMsg = ''; titleNormalizeMsg = ''" :aria-label="t('common.close')"><X :size="16" /></button>
      </div>
      <div v-if="titleSaveError" class="alert-banner error mb-3">
        {{ titleSaveError }}
        <button class="close-btn" @click="titleSaveError = ''" :aria-label="t('common.close')"><X :size="16" /></button>
      </div>

      <DataTable
        :value="titleRules"
        class="memphis-datatable"
        stripedRows
        :rowClass="titleRowClass"
      >
        <Column field="process" :header="t('tags.titleRule.column.process')" style="min-width: 140px">
          <template #body="{ data }">
            <input
              v-model="data.process"
              class="memphis-input compact"
              :placeholder="t('tags.titleRule.placeholder.process')"
              @input="markDirty(data)"
            />
          </template>
        </Column>
        <Column field="title" :header="t('tags.titleRule.column.fixedTitle')" style="min-width: 180px">
          <template #body="{ data }">
            <input
              v-model="data.title"
              class="memphis-input compact"
              :placeholder="t('tags.placeholder.any')"
              @input="markDirty(data)"
            />
          </template>
        </Column>
        <Column field="titleRegex" :header="t('tags.titleRule.column.regex')" style="min-width: 180px">
          <template #body="{ data }">
            <input
              v-model="data.titleRegex"
              class="memphis-input compact"
              :placeholder="t('tags.placeholder.any')"
              @input="markDirty(data)"
            />
          </template>
        </Column>
        <Column field="titleReplacement" :header="t('tags.titleRule.column.replacement')" style="min-width: 140px">
          <template #body="{ data }">
            <input
              v-model="data.titleReplacement"
              class="memphis-input compact"
              :placeholder="t('tags.placeholder.any')"
              @input="markDirty(data)"
            />
          </template>
        </Column>
        <Column field="applyOnWrite" :header="t('tags.titleRule.column.applyOnWrite')" style="width: 110px">
          <template #body="{ data }">
            <button
              class="icon-toggle"
              :class="{ on: data.applyOnWrite }"
              @click="data.applyOnWrite = !data.applyOnWrite; markDirty(data)"
              :title="data.applyOnWrite ? t('common.enabled') : t('common.disabled')"
              :aria-pressed="data.applyOnWrite"
            >
              <Check v-if="data.applyOnWrite" :size="18" />
              <X v-else :size="18" />
            </button>
          </template>
        </Column>
        <Column style="width: 60px">
          <template #body="{ index }">
            <button
              class="icon-btn danger"
              @click="removeTitleRule(index)"
              :title="t('common.delete')"
              :aria-label="t('common.delete')"
            >
              <Trash2 :size="18" />
            </button>
          </template>
        </Column>
        <template #empty>
          <div class="empty-state">{{ t('tags.titleRule.noRules') }}</div>
        </template>
      </DataTable>
    </MemphisCard>
  </div>
</template>

<script setup>
import { ref, computed, inject, onMounted, onUnmounted } from 'vue'
import { useI18n } from '../i18n/index.js'
import MemphisCard from '../components/MemphisCard.vue'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import { Plus, Save, Trash2, History, Check, X } from '@lucide/vue'

const apiBase = inject('apiBase')
const { t } = useI18n()

const rules = ref([])
const lastTagsWrite = ref('')
const tagError = ref('')
const savedMsg = ref('')
const saveError = ref('')
const saving = ref(false)
const titleRules = ref([])
const lastTitleWrite = ref('')
const titleRulesError = ref('')
const titleSavedMsg = ref('')
const titleSaveError = ref('')
const titleSaving = ref(false)
const titleNormalizing = ref(false)
const titleNormalizeMsg = ref('')
const tagsDirty = ref(false)
const titleDirty = ref(false)

const newTitleCount = computed(() => titleRules.value.filter(r => !r._saved && r.process).length)

const modes = [
  { v: 'Coexist', label: t('tags.mode.coexist') },
  { v: 'Overwrite', label: t('tags.mode.overwrite') },
]

const newCount = computed(() => rules.value.filter(r => !r._saved && r.tag).length)

let timer = null

function startPolling() {
  stopPolling()
  loadRules()
  timer = setInterval(loadRules, 2000)
}

function stopPolling() {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
}

function handleVisibility() {
  if (document.hidden) stopPolling()
  else startPolling()
}

onMounted(() => {
  startPolling()
  document.addEventListener('visibilitychange', handleVisibility)
})

onUnmounted(() => {
  stopPolling()
  document.removeEventListener('visibilitychange', handleVisibility)
})

function key(r) {
  return `${r.tag}|${r.process}|${r.titlePattern}`
}

function rowClass(data) {
  return data._saved ? '' : 'unsaved-row'
}

function markDirty(row) {
  row._saved = false
}

async function loadRules() {
  try {
    const r = await fetch(`${apiBase}/api/tags/status`)
    if (!r.ok) return
    const data = await r.json()

    // ---- tags.json 区块 ----
    const tagsWrite = data.tags?.lastWrite || ''
    tagError.value = data.tags?.error || ''
    if (tagsWrite !== lastTagsWrite.value || !tagsWrite) {
      lastTagsWrite.value = tagsWrite
      const serverRules = (data.tags?.rules || []).map(rule => ({
        tag: rule.tag || '',
        process: rule.process || '',
        titlePattern: rule.titlePattern || '',
        weight: rule.weight ?? 0,
        mode: rule.mode || 'Coexist',
        _saved: true,
      }))
      const serverKeys = new Set(serverRules.map(k => key(k)))
      const unsaved = rules.value.filter(r => !r._saved && r.tag && !serverKeys.has(key(r)))
      rules.value = [...serverRules, ...unsaved]
    }

    // ---- title_rules.json 区块 ----
    const titleWrite = data.titleRules?.lastWrite || ''
    titleRulesError.value = data.titleRules?.error || ''
    if (titleWrite !== lastTitleWrite.value || !titleWrite) {
      lastTitleWrite.value = titleWrite
      const serverTitleRules = (data.titleRules?.rules || []).map(rule => ({
        process: rule.process || '',
        title: rule.title || '',
        titleRegex: rule.titleRegex || '',
        titleReplacement: rule.titleReplacement || '',
        applyOnWrite: !!rule.applyOnWrite,
        _saved: true,
      }))
      const serverTitleKeys = new Set(serverTitleRules.map(r => titleKey(r)))
      const unsavedTitle = titleRules.value.filter(r => !r._saved && r.process && !serverTitleKeys.has(titleKey(r)))
      titleRules.value = [...serverTitleRules, ...unsavedTitle]
    }
  } catch (e) {
    console.error('loadRules:', e)
  }
}

function addRule() {
  rules.value.push({
    tag: '',
    process: '',
    titlePattern: '',
    weight: 0,
    mode: 'Coexist',
    _saved: false,
  })
}

function removeRule(index) {
  if (rules.value[index]?._saved) tagsDirty.value = true
  rules.value.splice(index, 1)
}

function addTitleRule() {
  titleRules.value.push({
    process: '',
    title: '',
    titleRegex: '',
    titleReplacement: '',
    applyOnWrite: false,
    _saved: false,
  })
}

function removeTitleRule(index) {
  if (titleRules.value[index]?._saved) titleDirty.value = true
  titleRules.value.splice(index, 1)
}

function titleKey(r) {
  return `${r.process}|${r.title}|${r.titleRegex}|${r.titleReplacement}|${r.applyOnWrite}`
}

function titleRowClass(data) {
  return data._saved ? '' : 'unsaved-row'
}

async function saveRules() {
  saving.value = true
  savedMsg.value = ''
  saveError.value = ''
  try {
    const body = rules.value
      .filter(r => r.tag)
      .map(r => ({
        tag: r.tag,
        process: r.process || null,
        titlePattern: r.titlePattern || null,
        weight: r.weight,
        mode: r.mode,
      }))
    const r = await fetch(`${apiBase}/api/tags/save`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    if (r.ok) {
      const data = await r.json()
      savedMsg.value = data.message || t('tags.savedCount', { count: data.saved })
      tagsDirty.value = false
      lastTagsWrite.value = ''
      await loadRules()
    } else {
      const data = await r.json().catch(() => ({}))
      saveError.value = data.error || t('tags.error.saveFailed', { status: r.status })
    }
  } catch (e) {
    saveError.value = t('tags.error.backendUnreachable', { message: e.message })
  }
  saving.value = false
}

async function saveTitleRules() {
  titleSaving.value = true
  titleSavedMsg.value = ''
  titleSaveError.value = ''
  titleNormalizeMsg.value = ''
  try {
    const body = titleRules.value
      .filter(r => r.process)
      .map(r => ({
        process: r.process,
        title: r.title || null,
        titleRegex: r.titleRegex || null,
        titleReplacement: r.titleReplacement || null,
        applyOnWrite: !!r.applyOnWrite,
      }))
    const r = await fetch(`${apiBase}/api/title-rules/save`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    if (r.ok) {
      const data = await r.json()
      titleSavedMsg.value = t('tags.titleRule.savedCount', { count: data.saved })
      titleDirty.value = false
      lastTitleWrite.value = ''
      await loadRules()
    } else {
      const data = await r.json().catch(() => ({}))
      titleSaveError.value = data.error || t('tags.error.saveFailed', { status: r.status })
    }
  } catch (e) {
    titleSaveError.value = t('tags.error.backendUnreachable', { message: e.message })
  }
  titleSaving.value = false
}

async function normalizeDb() {
  if (!window.confirm(t('tags.titleRule.normalizeConfirm'))) return
  titleNormalizing.value = true
  titleSavedMsg.value = ''
  titleSaveError.value = ''
  titleNormalizeMsg.value = ''
  try {
    const r = await fetch(`${apiBase}/api/title-rules/normalize-db`, { method: 'POST' })
    if (r.ok) {
      const data = await r.json()
      titleNormalizeMsg.value = t('tags.titleRule.normalizeResult', {
        total: data.totalRows,
        modified: data.modifiedRows,
      })
    } else {
      const data = await r.json().catch(() => ({}))
      titleSaveError.value = data.error || t('tags.error.saveFailed', { status: r.status })
    }
  } catch (e) {
    titleSaveError.value = t('tags.error.backendUnreachable', { message: e.message })
  }
  titleNormalizing.value = false
}
</script>

<style lang="scss" scoped>
.tags-page {
  width: 100%;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 700;
  letter-spacing: 1px;
  margin-bottom: 24px;
  color: var(--text-color);
}

.alert-banner {
  padding: 12px 16px;
  border: 2px solid var(--border-color);
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-weight: 600;

  &.success {
    background: var(--success-color);
    color: white;
  }

  &.error {
    background: var(--danger-color);
    color: white;
  }
}

.close-btn {
  background: transparent;
  border: none;
  color: white;
  font-size: 1.2rem;
  cursor: pointer;
  padding: 0 8px;
}

.rules-card {
  min-height: 400px;
}

.title-rules-card {
  margin-top: 24px;
}

.card-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 16px;
}

.card-title {
  font-size: 1.1rem;
  font-weight: 600;
  letter-spacing: 0.5px;
  color: var(--text-color);
}

.unsaved-count {
  font-size: 0.85rem;
  color: var(--warning-color);
  margin-left: 12px;
  font-weight: normal;
}

.actions {
  display: flex;
  gap: 8px;
}

.memphis-input,
.memphis-select {
  width: 100%;
  padding: 6px 10px;
  border: 2px solid var(--surface-200);
  background: var(--surface-card);
  color: var(--text-color);
  font-weight: 600;
  transition: border-color 0.2s ease;

  &.compact {
    font-size: 0.9rem;
    font-family: 'Ubuntu Mono', 'Consolas', monospace;
  }

  &:focus {
    outline: none;
    border-color: var(--primary-color);
  }
}

.empty-state {
  text-align: center;
  padding: 32px;
  color: var(--surface-400);
  font-style: italic;
}

:deep(.memphis-datatable) {
  .unsaved-row {
    background: rgba(155, 93, 229, 0.1);
    border-left: 4px solid var(--warning-color);
  }
}
</style>
