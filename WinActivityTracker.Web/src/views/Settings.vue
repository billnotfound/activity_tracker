<!--
  Settings page — full configuration UI for the backend.
  Loads current settings on mount, allows editing all fields, saves via PUT /api/settings.
  Changes take effect immediately (trackers read settings each poll cycle).

  Fields:
    - Tracking enabled: master on/off switch for all trackers
    - Poll intervals: window, process, media (seconds)
    - Idle threshold: minutes before AFK detection
    - Excluded processes: comma-separated list of process names to ignore
    - Data retention: days before /api/db/cleanup deletes records
-->
<template>
  <div>
    <h4 class="mb-3">后端配置</h4>

    <!-- Status bar -->
    <div class="alert" :class="statusClass" role="alert">
      {{ statusText }}
      <span v-if="saving" class="spinner-border spinner-border-sm ms-2"></span>
    </div>

    <div class="row">
      <div class="col-lg-8">
        <div class="card mb-3">
          <div class="card-header">追踪控制</div>
          <div class="card-body">
            <div class="form-check form-switch mb-3">
              <input class="form-check-input" type="checkbox" id="trackingEnabled" v-model="form.trackingEnabled" />
              <label class="form-check-label" for="trackingEnabled">
                <strong>{{ form.trackingEnabled ? '追踪已启用' : '追踪已暂停' }}</strong>
              </label>
              <div class="form-text">关闭后所有追踪器停止记录，API 仍然可用。相当于"暂停"功能。</div>
            </div>

            <div class="row">
              <div class="col-md-4 mb-3">
                <label class="form-label">窗口轮询间隔 (秒)</label>
                <input type="number" class="form-control" v-model.number="form.windowPollSeconds" min="1" />
                <div class="form-text">默认 3s。影响焦点切换检测精度。</div>
              </div>
              <div class="col-md-4 mb-3">
                <label class="form-label">进程轮询间隔 (秒)</label>
                <input type="number" class="form-control" v-model.number="form.processPollSeconds" min="5" />
                <div class="form-text">默认 30s。枚举所有进程较耗性能。</div>
              </div>
              <div class="col-md-4 mb-3">
                <label class="form-label">媒体轮询间隔 (秒)</label>
                <input type="number" class="form-control" v-model.number="form.mediaPollSeconds" min="1" />
                <div class="form-text">默认 5s。歌曲切换检测频率。</div>
              </div>
            </div>

            <div class="mb-3">
              <label class="form-label">空闲判定阈值 (分钟)</label>
              <input type="number" class="form-control w-auto" v-model.number="form.idleThresholdMinutes" min="1" />
              <div class="form-text">超过此时长无键鼠操作即判定为空闲，暂停焦点追踪。默认 2 分钟。</div>
            </div>
            <div class="form-check mb-2">
              <input class="form-check-input" type="checkbox" id="fullscreenBypass" v-model="form.fullscreenBypassIdle" />
              <label class="form-check-label" for="fullscreenBypass">
                全屏时绕过空闲检测
              </label>
              <div class="form-text">启用后，全屏/最大化窗口（游戏、视频）不会因无操作而暂停追踪。</div>
            </div>
            <div class="form-check mb-2">
              <input class="form-check-input" type="checkbox" id="mergeSwitches" v-model="form.mergeSameProcessSwitches" />
              <label class="form-check-label" for="mergeSwitches">
                同程序连续切换合并计数
              </label>
              <div class="form-text">启用后，Firefox 切 3 个 tab 只计 1 次切换。影响切换次数显示。</div>
            </div>
          </div>
        </div>

        <div class="card mb-3">
          <div class="card-header">进程排除</div>
          <div class="card-body">
            <label class="form-label">排除的进程名 (逗号分隔，不区分大小写)</label>
            <input type="text" class="form-control" v-model="excludeText" placeholder="explorer, SearchApp, TextInputHost" />
            <div class="form-text">这些进程将从焦点、窗口快照、后台进程和媒体记录中全部过滤。</div>
          </div>
        </div>

        <div class="card mb-3">
          <div class="card-header">服务器</div>
          <div class="card-body">
            <div class="mb-3">
              <label class="form-label">API 端口 <span class="text-muted">(重启后生效)</span></label>
              <input type="number" class="form-control w-auto" v-model.number="form.apiPort" min="1024" max="65535" />
              <div class="form-text">默认 5200。修改后需重启 Service 才能生效。</div>
            </div>
          </div>
        </div>

        <div class="card mb-3">
          <div class="card-header">数据库</div>
          <div class="card-body">
            <div class="mb-3">
              <label class="form-label">数据保留天数</label>
              <input type="number" class="form-control w-auto" v-model.number="form.dataRetentionDays" min="1" />
              <div class="form-text">POST /api/db/cleanup 的默认清理阈值。</div>
            </div>
            <div class="mb-3">
              <button class="btn btn-outline-secondary btn-sm me-2" @click="loadDbStats">
                刷新统计
              </button>
              <button class="btn btn-outline-danger btn-sm me-2" @click="runCleanup" :disabled="cleaning">
                {{ cleaning ? '清理中...' : '立即清理旧数据' }}
              </button>
              <button v-if="!resetConfirm" class="btn btn-outline-danger btn-sm" @click="resetConfirm = true">
                删除全部数据...
              </button>
              <span v-else>
                <button class="btn btn-danger btn-sm me-1" @click="runReset" :disabled="resetting">
                  {{ resetting ? '删除中...' : '确认删除全部' }}
                </button>
                <button class="btn btn-outline-secondary btn-sm" @click="resetConfirm = false">取消</button>
              </span>
            </div>
            <div v-if="dbStats" class="small">
              <table class="table table-sm">
                <tr><td>焦点变化记录</td><td>{{ dbStats.focusChanges?.toLocaleString() }}</td></tr>
                <tr><td>窗口快照 (旧)</td><td>{{ dbStats.windowSnapshots?.toLocaleString() }}</td></tr>
                <tr><td>窗口会话 (新)</td><td>{{ dbStats.windowSessions?.toLocaleString() }}</td></tr>
                <tr><td>后台进程快照</td><td>{{ dbStats.processSnapshots?.toLocaleString() }}</td></tr>
                <tr><td>媒体记录</td><td>{{ dbStats.mediaRecords }}</td></tr>
                <tr><td>最早记录</td><td>{{ fmtLocal(dbStats.oldestRecord) }}</td></tr>
              </table>
            </div>
            <div v-if="cleanupResult" class="alert alert-success small mt-2">
              清理完成：删除 {{ cleanupResult.deleted.focusChanges }} 条焦点变化、
              {{ cleanupResult.deleted.windowSnapshots }} 条窗口快照、
              {{ cleanupResult.deleted.processSnapshots }} 条进程快照、
              {{ cleanupResult.deleted.mediaRecords }} 条媒体记录。
            </div>
            <div v-if="resetResult" class="alert alert-warning small mt-2">
              {{ resetResult.message }}：删除 {{ resetResult.deleted.focusChanges }} 条焦点变化、
              {{ resetResult.deleted.windowSnapshots }} 条窗口快照、
              {{ resetResult.deleted.processSnapshots }} 条进程快照、
              {{ resetResult.deleted.mediaRecords }} 条媒体记录。
            </div>
          </div>
        </div>

        <button class="btn btn-primary btn-lg" @click="saveSettings" :disabled="saving">
          {{ saving ? '保存中...' : '保存设置' }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, inject, onMounted, computed } from 'vue'

const apiBase = inject('apiBase')

const form = reactive({
  trackingEnabled: true,
  fullscreenBypassIdle: true,
  mergeSameProcessSwitches: true,
  windowPollSeconds: 3,
  processPollSeconds: 30,
  mediaPollSeconds: 5,
  idleThresholdMinutes: 2,
  dataRetentionDays: 90,
  apiPort: 5200
})
const excludeText = ref('')
const saving = ref(false)
const cleaning = ref(false)
const dbStats = ref(null)
const cleanupResult = ref(null)
const resetConfirm = ref(false)
const resetting = ref(false)
const resetResult = ref(null)
const statusOk = ref(false)

const statusClass = computed(() => statusOk.value ? 'alert-success' : 'alert-warning')
const statusText = computed(() => statusOk.value ? '已连接' : '未连接后端 — 检查服务是否启动')

onMounted(async () => {
  await loadSettings()
  await loadDbStats()
})

async function loadSettings() {
  try {
    const r = await fetch(`${apiBase}/api/status`)
    if (!r.ok) return
    const s = await r.json()
    statusOk.value = true
    form.trackingEnabled = s.trackingEnabled
  } catch { statusOk.value = false }

  try {
    const r = await fetch(`${apiBase}/api/settings`)
    if (!r.ok) return
    const s = await r.json()
    form.trackingEnabled = s.trackingEnabled
    form.fullscreenBypassIdle = s.fullscreenBypassIdle ?? true
    form.mergeSameProcessSwitches = s.mergeSameProcessSwitches ?? true
    form.windowPollSeconds = s.windowPollSeconds
    form.processPollSeconds = s.processPollSeconds
    form.mediaPollSeconds = s.mediaPollSeconds
    form.idleThresholdMinutes = s.idleThresholdMinutes
    form.dataRetentionDays = s.dataRetentionDays
    form.apiPort = s.apiPort || 5200
    excludeText.value = (s.excludedProcesses || []).join(', ')
    statusOk.value = true
  } catch { statusOk.value = false }
}

async function saveSettings() {
  saving.value = true
  try {
    const body = {
      ...form,
      excludedProcesses: excludeText.value
        .split(',')
        .map(s => s.trim())
        .filter(s => s.length > 0)
    }
    const r = await fetch(`${apiBase}/api/settings`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    })
    if (r.ok) {
      const s = await r.json()
      excludeText.value = (s.excludedProcesses || []).join(', ')
      statusOk.value = true
    } else {
      const err = await r.text()
      console.error('Save failed:', r.status, err)
      alert(`保存失败 (${r.status}): ${err}`)
    }
  } catch (e) {
    console.error('Save error:', e)
    alert(`无法连接后端: ${e.message}`)
  }
  saving.value = false
}

async function loadDbStats() {
  try {
    const r = await fetch(`${apiBase}/api/db/stats`)
    if (r.ok) dbStats.value = await r.json()
  } catch {}
}

async function runCleanup() {
  cleaning.value = true
  cleanupResult.value = null
  try {
    const r = await fetch(`${apiBase}/api/db/cleanup?days=${form.dataRetentionDays}`, { method: 'POST' })
    if (r.ok) cleanupResult.value = await r.json()
  } catch {}
  cleaning.value = false
  await loadDbStats()
}

async function runReset() {
  resetting.value = true
  resetResult.value = null
  try {
    const r = await fetch(`${apiBase}/api/db/reset?confirm=true`, { method: 'POST' })
    if (r.ok) resetResult.value = await r.json()
  } catch {}
  resetting.value = false
  resetConfirm.value = false
  await loadDbStats()
}

// DB timestamps are UTC but may lack 'Z' suffix (EF Core strips Kind).
// Append 'Z' so JS parses as UTC, then format in local time.
function fmtLocal(ts) {
  if (!ts) return '-'
  const t = ts.endsWith('Z') ? ts : ts + 'Z'
  return new Date(t).toLocaleString()
}
</script>
