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
      <button class="close-btn" @click="savedMsg = ''">✕</button>
    </div>
    <div v-if="saveError" class="alert-banner error mb-3">
      {{ saveError }}
      <button class="close-btn" @click="saveError = ''">✕</button>
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
          <button class="action-btn secondary" @click="addRule">
            <i class="pi pi-plus"></i>
            {{ t('tags.addRule') }}
          </button>
          <button
            class="action-btn primary"
            @click="saveRules"
            :disabled="saving || !newCount"
          >
            <i class="pi pi-save"></i>
            {{ saving ? t('common.saving') : t('common.save') }}
          </button>
        </div>
      </div>

      <DataTable
        :value="rules"
        class="memphis-datatable"
        stripedRows
        :rowClass="rowClass"
      >
        <Column field="tag" header="Tag" style="min-width: 120px">
          <template #body="{ data }">
            <input
              v-model="data.tag"
              class="memphis-input compact"
              placeholder="tag"
            />
          </template>
        </Column>
        <Column field="process" header="Process" style="min-width: 150px">
          <template #body="{ data }">
            <input
              v-model="data.process"
              class="memphis-input compact"
              placeholder="(any)"
            />
          </template>
        </Column>
        <Column field="titlePattern" header="Title Pattern" style="min-width: 180px">
          <template #body="{ data }">
            <input
              v-model="data.titlePattern"
              class="memphis-input compact"
              placeholder="(any)"
            />
          </template>
        </Column>
        <Column field="weight" header="Weight" style="width: 100px">
          <template #body="{ data }">
            <input
              v-model.number="data.weight"
              type="number"
              class="memphis-input compact"
            />
          </template>
        </Column>
        <Column field="mode" header="Mode" style="width: 120px">
          <template #body="{ data }">
            <select v-model="data.mode" class="memphis-select compact">
              <option v-for="m in modes" :key="m.v" :value="m.v">
                {{ m.label }}
              </option>
            </select>
          </template>
        </Column>
        <Column style="width: 60px">
          <template #body="{ index }">
            <button
              class="delete-btn"
              @click="rules.splice(index, 1)"
              :title="t('common.delete')"
            >
              <i class="pi pi-trash"></i>
            </button>
          </template>
        </Column>
        <template #empty>
          <div class="empty-state">{{ t('tags.noRules') }}</div>
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

const apiBase = inject('apiBase')
const { t } = useI18n()

const rules = ref([])
const lastTagsWrite = ref('')
const tagError = ref('')
const savedMsg = ref('')
const saveError = ref('')
const saving = ref(false)
const modes = [
  { v: 'Coexist', label: t('tags.mode.coexist') },
  { v: 'Overwrite', label: t('tags.mode.overwrite') },
]

const newCount = computed(() => rules.value.filter(r => !r._saved).length)

let timer = null

onMounted(() => {
  loadRules()
  timer = setInterval(loadRules, 2000)
})

onUnmounted(() => clearInterval(timer))

function key(r) {
  return `${r.tag}|${r.process}|${r.titlePattern}`
}

function rowClass(data) {
  return data._saved ? '' : 'unsaved-row'
}

async function loadRules() {
  try {
    const r = await fetch(`${apiBase}/api/tags/status`)
    if (!r.ok) return
    const data = await r.json()
    const newWrite = data.tags?.lastWrite || ''
    tagError.value = data.tags?.error || ''

    if (newWrite === lastTagsWrite.value && newWrite) return
    lastTagsWrite.value = newWrite

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
</script>

<style lang="scss" scoped>
.tags-page {
  width: 100%;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 700;
  text-transform: uppercase;
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
  text-transform: uppercase;
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

.action-btn {
  padding: 8px 16px;
  border: 2px solid var(--primary-color);
  background: transparent;
  color: var(--text-color);
  font-weight: 600;
  text-transform: uppercase;
  font-size: 0.85rem;
  letter-spacing: 0.5px;
  cursor: pointer;
  transition: all 0.2s ease;
  display: flex;
  align-items: center;
  gap: 6px;

  &:hover:not(:disabled) {
    background: var(--primary-color);
    color: var(--surface-card);
    transform: translateY(-2px);
    box-shadow: 0 4px 0 var(--primary-color);
  }

  &:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }

  &.secondary {
    border-color: var(--secondary-color);

    &:hover:not(:disabled) {
      background: var(--secondary-color);
      box-shadow: 0 4px 0 var(--secondary-color);
    }
  }
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
  }

  &:focus {
    outline: none;
    border-color: var(--primary-color);
  }
}

.delete-btn {
  padding: 6px 10px;
  border: 2px solid var(--danger-color);
  background: transparent;
  color: var(--danger-color);
  cursor: pointer;
  transition: all 0.2s ease;
  display: flex;
  align-items: center;
  justify-content: center;

  &:hover {
    background: var(--danger-color);
    color: white;
    transform: translateY(-2px);
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
