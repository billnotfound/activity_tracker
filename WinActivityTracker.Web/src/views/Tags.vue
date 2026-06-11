<template>
  <div>
    <h4 class="mb-3">{{ t('tags.pageTitle') }}</h4>

    <div v-if="tagError" class="alert alert-danger small" role="alert">
      <strong>{{ t('tags.tagsError') }}</strong> {{ tagError }}
    </div>
    <div v-if="savedMsg" class="alert alert-success alert-dismissible fade show small" role="alert">
      {{ savedMsg }}
      <button type="button" class="btn-close" @click="savedMsg=''"></button>
    </div>
    <div v-if="saveError" class="alert alert-danger alert-dismissible fade show small" role="alert">
      {{ saveError }}
      <button type="button" class="btn-close" @click="saveError=''"></button>
    </div>

    <div class="row">
      <div class="col">
        <div class="card mb-3">
          <div class="card-header d-flex justify-content-between align-items-center">
            <span>{{ t('tags.card.ruleList', { count: rules.length }) }}
              <small class="text-muted"><span v-if="newCount">{{ t('tags.unsavedCount', { count: newCount }) }}</span></small>
            </span>
            <div>
              <button class="btn btn-sm btn-outline-secondary me-1" @click="addRule">{{ t('tags.addRule') }}</button>
              <button class="btn btn-sm btn-primary" @click="saveRules" :disabled="saving || !newCount">
                {{ saving ? t('common.saving') : t('common.save') }}
              </button>
            </div>
          </div>
          <div class="card-body p-0">
            <table class="table table-sm mb-0">
              <thead>
                <tr>
                  <th>tag</th>
                  <th>process</th>
                  <th>titlePattern</th>
                  <th style="width:80px">weight</th>
                  <th style="width:100px">mode</th>
                  <th style="width:50px"></th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(r, i) in rules" :key="i"
                  :class="{ 'table-warning': !r._saved }"
                  :title="r._saved ? '' : t('tags.unsavedRule')">
                  <td><input v-model="r.tag" class="form-control form-control-sm" style="width:90px" /></td>
                  <td><input v-model="r.process" class="form-control form-control-sm" style="width:140px" placeholder="(any)" /></td>
                  <td><input v-model="r.titlePattern" class="form-control form-control-sm" style="width:160px" placeholder="(any)" /></td>
                  <td><input v-model.number="r.weight" type="number" class="form-control form-control-sm" style="width:70px" /></td>
                  <td>
                    <select v-model="r.mode" class="form-select form-select-sm">
                      <option v-for="m in modes" :key="m.v" :value="m.v">{{ m.label }}</option>
                    </select>
                  </td>
                  <td><button class="btn btn-sm btn-outline-danger" @click="rules.splice(i,1)" :title="t('common.delete')">✕</button></td>
                </tr>
                <tr v-if="!rules.length"><td colspan="6" class="text-muted text-center">{{ t('tags.noRules') }}</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, inject, onMounted, onUnmounted } from 'vue'
import { useI18n } from '../i18n/index.js'

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
  { v: 'Overwrite', label: t('tags.mode.overwrite') }
]

const newCount = computed(() => rules.value.filter(r => !r._saved).length)

let timer = null

onMounted(() => { loadRules(); timer = setInterval(loadRules, 2000) })
onUnmounted(() => clearInterval(timer))

function key(r) { return `${r.tag}|${r.process}|${r.titlePattern}` }

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
      _saved: true
    }))
    const serverKeys = new Set(serverRules.map(k => key(k)))

    const unsaved = rules.value.filter(r => !r._saved && r.tag && !serverKeys.has(key(r)))

    rules.value = [...serverRules, ...unsaved]
  } catch (e) { console.error('loadRules:', e) }
}

function addRule() {
  rules.value.push({ tag: '', process: '', titlePattern: '', weight: 0, mode: 'Coexist', _saved: false })
}

async function saveRules() {
  saving.value = true
  savedMsg.value = ''
  saveError.value = ''
  try {
    const body = rules.value
      .filter(r => r.tag)
      .map(r => ({ tag: r.tag, process: r.process || null, titlePattern: r.titlePattern || null, weight: r.weight, mode: r.mode }))
    const r = await fetch(`${apiBase}/api/tags/save`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
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
  } catch (e) { saveError.value = t('tags.error.backendUnreachable', { message: e.message }) }
  saving.value = false
}
</script>
