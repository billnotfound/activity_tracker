<template>
  <button type="button" class="p-button p-button-secondary" @click="open">
    <Upload :size="16" aria-hidden="true" />{{ t('settings.import.title') }}
  </button>
  <Teleport to="body">
    <div v-if="visible" class="import-backdrop" @pointerdown.self="close">
      <section ref="dialog" class="import-dialog" role="dialog" aria-modal="true" :aria-label="t('settings.import.title')" tabindex="-1">
        <header>
          <h2>{{ t('settings.import.title') }}</h2>
          <button type="button" class="icon-btn" :aria-label="t('common.close')" :disabled="busy" @click="close"><X :size="18" /></button>
        </header>
        <p>{{ t('settings.import.help') }}</p>
        <label class="file-field">
          <span>{{ t('settings.import.choose') }}</span>
          <input type="file" accept=".db,.sqlite,.sqlite3,application/vnd.sqlite3" :disabled="busy" @change="selectFile" />
        </label>
        <UiNotice v-if="error" tone="error">{{ error }}</UiNotice>
        <UiNotice v-if="imported" tone="success">{{ t('settings.import.done', { count: formatNumber(total) }) }}</UiNotice>
        <template v-if="result">
          <div class="import-table-wrap">
            <table class="import-table">
              <thead><tr>
                <th scope="col">{{ t('settings.import.category') }}</th>
                <th scope="col">{{ t('settings.import.range') }}</th>
                <th scope="col">{{ t('settings.import.source') }}</th>
                <th scope="col">{{ t(imported ? 'settings.import.added' : 'settings.import.new') }}</th>
                <th scope="col">{{ t('settings.import.duplicate') }}</th>
                <th scope="col">{{ t('settings.import.invalid') }}</th>
              </tr></thead>
              <tbody><tr v-for="table in result.tables" :key="table.table">
                <th scope="row">{{ t(`settings.import.table.${table.table}`) }}</th>
                <td>{{ table.from ? `${formatDate(table.from)} — ${formatDate(table.to)}` : '—' }}</td>
                <td>{{ formatNumber(table.sourceRows) }}</td>
                <td>{{ formatNumber(table.added) }}</td>
                <td>{{ formatNumber(table.duplicate) }}</td>
                <td>{{ formatNumber(table.invalid) }}</td>
              </tr></tbody>
            </table>
          </div>
          <details v-if="samples.length">
            <summary>{{ t('settings.import.samples') }}</summary>
            <ul><li v-for="(sample, index) in samples" :key="index">
              {{ formatDate(sample.timestamp, true) }} · {{ t(`settings.import.table.${sample.table}`) }} · {{ sample.label || '—' }}
            </li></ul>
          </details>
          <p v-if="!imported && total === 0">{{ t('settings.import.empty') }}</p>
        </template>
        <footer>
          <span v-if="busy" role="status">{{ t(busy === 'import' ? 'settings.import.importing' : 'settings.import.reading') }}</span>
          <button v-else-if="!result && file" type="button" class="p-button" @click="run(false)">{{ t('settings.import.preview') }}</button>
          <button v-else-if="result && !imported && total > 0" type="button" class="p-button" @click="run(true)">
            {{ t('settings.import.confirm', { count: formatNumber(total) }) }}
          </button>
          <button type="button" class="p-button p-button-secondary" :disabled="busy" @click="close">{{ t('common.close') }}</button>
        </footer>
      </section>
    </div>
  </Teleport>
</template>

<script setup>
import { computed, inject, onBeforeUnmount, ref, shallowRef } from 'vue'
import { Upload, X } from '@lucide/vue'
import { useI18n } from '../i18n/index.js'
import { useModalFocus } from '../composables/useModalFocus.js'
import UiNotice from './UiNotice.vue'

const { t } = useI18n()
const emit = defineEmits(['imported'])
const apiBase = inject('apiBase', '')
const visible = ref(false)
const dialog = ref(null)
const file = shallowRef(null)
const result = ref(null)
const busy = ref('')
const error = ref('')
const imported = ref(false)
let request

const total = computed(() => result.value?.tables.reduce((sum, table) => sum + table.added, 0) || 0)
const samples = computed(() => result.value?.tables.flatMap(table => table.samples.map(sample => ({ ...sample, table: table.table }))) || [])
const formatNumber = value => value.toLocaleString()
function formatDate(value, includeTime = false) {
  const date = new Date(value.replace(' ', 'T') + 'Z')
  return includeTime ? date.toLocaleString() : date.toLocaleDateString()
}
function open() {
  file.value = result.value = null
  error.value = ''
  imported.value = false
  visible.value = true
}
function close() {
  if (!busy.value) visible.value = false
}
function selectFile(event) {
  file.value = event.target.files?.[0] || null
  result.value = null
  imported.value = false
  error.value = ''
  if (file.value) run(false)
}
async function run(commit) {
  if (busy.value || !file.value || (commit && !result.value)) return
  busy.value = commit ? 'import' : 'preview'
  error.value = ''
  request = new AbortController()
  try {
    const route = commit ? `?sha256=${encodeURIComponent(result.value.sha256)}` : '/preview'
    const response = await fetch(`${apiBase}/api/db/import${route}`, {
      method: 'POST', headers: { 'Content-Type': 'application/octet-stream' },
      body: file.value, signal: request.signal,
    })
    const payload = await response.json().catch(() => ({}))
    if (!response.ok) {
      const codes = ['fileTooLarge', 'busy', 'fileChanged', 'invalidDatabase', 'unsupportedSchema']
      error.value = t(`settings.import.error.${codes.includes(payload.code) ? payload.code : 'failed'}`)
      if (payload.code === 'fileChanged') result.value = null
      return
    }
    result.value = payload
    if (commit) {
      imported.value = true
      emit('imported')
    }
  } catch (e) {
    if (e.name !== 'AbortError') error.value = t('settings.import.error.failed')
  } finally {
    request = null
    busy.value = ''
  }
}
useModalFocus(dialog, () => visible.value, close)
onBeforeUnmount(() => request?.abort())
</script>

<style scoped>
.import-backdrop { position: fixed; inset: 0; z-index: 2200; display: grid; place-items: center; padding: 16px; background: #0006; }
.import-dialog { width: min(820px, 100%); max-height: calc(100dvh - 32px); overflow: auto; background: var(--surface-card); border: 3px solid var(--text-color); box-shadow: 6px 6px 0 var(--primary-color); padding: 24px; }
header, footer { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
h2 { font-size: 1.3rem; }
p { margin: 12px 0; line-height: 1.5; }
.file-field { display: grid; gap: 8px; margin: 16px 0; }
.file-field input { max-width: 100%; }
.import-table-wrap { overflow-x: auto; margin: 16px 0; }
.import-table { width: 100%; border-collapse: collapse; font-size: .9rem; }
.import-table th, .import-table td { padding: 8px; border-bottom: 1px solid var(--surface-300); text-align: left; }
.import-table td:nth-child(n+3) { font-variant-numeric: tabular-nums; }
details { margin: 12px 0; }
summary { cursor: pointer; }
ul { padding-left: 1.4rem; line-height: 1.55; overflow-wrap: anywhere; }
footer { justify-content: flex-end; flex-wrap: wrap; margin-top: 16px; }
</style>
