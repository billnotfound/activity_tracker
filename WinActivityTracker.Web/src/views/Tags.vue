<template>
  <div>
    <h4 class="mb-3">标签规则 <small class="text-muted">(tags.json 热重载)</small></h4>

    <div v-if="tagError" class="alert alert-danger small" role="alert">
      <strong>tags.json 错误:</strong> {{ tagError }}
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
            <span>标签规则列表 <small class="text-muted">({{ rules.length }} 条
              <span v-if="newCount">，{{ newCount }} 条未保存</span>)
            </small></span>
            <div>
              <button class="btn btn-sm btn-outline-secondary me-1" @click="addRule">+ 新增</button>
              <button class="btn btn-sm btn-primary" @click="saveRules" :disabled="saving || !newCount">
                {{ saving ? '保存中...' : '保存' }}
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
                  :title="r._saved ? '' : '未保存的新规则'">
                  <td><input v-model="r.tag" class="form-control form-control-sm" style="width:90px" /></td>
                  <td><input v-model="r.process" class="form-control form-control-sm" style="width:140px" placeholder="(any)" /></td>
                  <td><input v-model="r.titlePattern" class="form-control form-control-sm" style="width:160px" placeholder="(any)" /></td>
                  <td><input v-model.number="r.weight" type="number" class="form-control form-control-sm" style="width:70px" /></td>
                  <td>
                    <select v-model="r.mode" class="form-select form-select-sm">
                      <option v-for="m in modes" :key="m.v" :value="m.v">{{ m.label }}</option>
                    </select>
                  </td>
                  <td><button class="btn btn-sm btn-outline-danger" @click="rules.splice(i,1)" title="删除">✕</button></td>
                </tr>
                <tr v-if="!rules.length"><td colspan="6" class="text-muted text-center">暂无规则</td></tr>
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

const apiBase = inject('apiBase')

const rules = ref([])
const lastTagsWrite = ref('')
const tagError = ref('')
const savedMsg = ref('')
const saveError = ref('')
const saving = ref(false)
const modes = [
  { v: 'Coexist', label: '共存 (coexist)' },
  { v: 'Overwrite', label: '覆盖 (overwrite)' }
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

    // Skip refresh if file hasn't changed and we have data
    if (newWrite === lastTagsWrite.value && newWrite) return
    lastTagsWrite.value = newWrite

    // Build server rule map, merge with unsaved local rules
    const serverRules = (data.tags?.rules || []).map(rule => ({
      tag: rule.tag || '',
      process: rule.process || '',
      titlePattern: rule.titlePattern || '',
      weight: rule.weight ?? 0,
      mode: rule.mode || 'Coexist',
      _saved: true
    }))
    const serverKeys = new Set(serverRules.map(k => key(k)))

    // Keep unsaved local rules that don't exist on server
    const unsaved = rules.value.filter(r => !r._saved && r.tag && !serverKeys.has(key(r)))

    // Merge: all server rules + unsaved local rules
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
      savedMsg.value = data.message || `已保存 ${data.saved} 条规则`
      lastTagsWrite.value = '' // force next refresh
      await loadRules()
    } else {
      const data = await r.json().catch(() => ({}))
      saveError.value = data.error || `保存失败 (${r.status})`
    }
  } catch (e) { saveError.value = '无法连接后端: ' + e.message }
  saving.value = false
}
</script>
