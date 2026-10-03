<!--
  Time Anomaly panel — /time
  Shows NTP reference status + the anomaly list.
  Pending and Confirmed anomalies let the user pick a direction (pre/post);
  Applied anomalies can be restored;
  Suspicious/Drift can be ignored.
-->
<template>
  <div class="time-anomaly-page" :class="{ embedded }">
    <h2 v-if="!embedded" class="page-title">{{ t('timeAnomaly.title') }}</h2>

    <!-- Current status card -->
    <section class="memphis-box status-card">
      <h3 class="section-title">{{ t('timeAnomaly.statusCard.title') }}</h3>
      <div class="current-time-state" :class="{ problem: activeTotal, unavailable: ntp.succeeded === false }">
        <TriangleAlert v-if="activeTotal" :size="24" />
        <CircleCheck v-else :size="24" />
        <div>
          <b>{{ activeTotal ? t('timeAnomaly.currentProblem', { count: activeTotal }) : t('timeAnomaly.currentClear') }}</b>
          <span v-if="ntp.succeeded === false">{{ t('timeAnomaly.referenceUnavailable') }}</span>
        </div>
      </div>
      <div class="status-grid">
        <div class="status-item">
          <span>{{ t('timeAnomaly.ntpEnabled') }}</span>
          <b>{{ ntp.useNtp ? '✓' : '✗' }}</b>
        </div>
        <div class="status-item">
          <span>{{ t('timeAnomaly.ntpLastQuery') }}</span>
          <b>{{ formatNtpTime(ntp.lastQueryAt) }}</b>
        </div>
        <div class="status-item">
          <span>{{ t('timeAnomaly.ntpOffset') }}</span>
          <b>{{ ntp.offsetSeconds != null ? ntp.offsetSeconds + 's' : '—' }}</b>
        </div>
        <div class="status-item">
          <span>{{ t('timeAnomaly.ntpLatency') }}</span>
          <b>{{ ntp.latencyMs != null ? ntp.latencyMs + 'ms' : '—' }}</b>
        </div>
      </div>
      <div class="status-actions">
        <Button :label="t('timeAnomaly.ntpCheckNow')" @click="checkNtp" :loading="checking" />
      </div>
    </section>

    <!-- Anomaly list -->
    <section class="memphis-box list-card">
      <h3 class="section-title">{{ t('timeAnomaly.list.title') }}</h3>
      <button v-if="!activeTotal" type="button" class="empty empty-action" @click="startManualCorrection">
        <CircleCheck :size="28" />
        <b>{{ t('timeAnomaly.currentClear') }}</b>
        <span>{{ t('timeAnomaly.emptyAction') }}</span>
      </button>
      <div v-for="a in activeItems" :key="a.id" class="anomaly-row" :data-status="a.status">
        <div class="anomaly-meta">
          <span class="badge" :class="'badge-' + a.status">{{ t('timeAnomaly.status.' + a.status) }}</span>
          <span>{{ sourceLabel(a.source) }}</span>
          <span>{{ formatOffset(a.offsetSeconds) }}</span>
          <span>{{ formatTime(a.detectedAt) }}</span>
        </div>
        <div class="time-change">
          <div class="time-point before">
            <span>{{ t('timeAnomaly.beforeChange') }}</span>
            <b>{{ timeChange(a).before }}</b>
          </div>
          <ArrowRight :size="20" class="time-arrow" />
          <div class="time-point after">
            <span>{{ t('timeAnomaly.afterChange') }}</span>
            <b>{{ timeChange(a).after }}</b>
          </div>
        </div>
        <span v-if="a.note" class="note">{{ a.note }}</span>
        <div class="actions">
          <!-- Pending → direction choice (pre/post) -->
          <template v-if="a.status === 'Pending'">
            <Button :label="t('timeAnomaly.direction.pre')" size="small" @click="apply(a, 'pre')" />
            <Button :label="t('timeAnomaly.direction.post')" size="small" @click="apply(a, 'post')" />
          </template>
          <!-- Confirmed → direction choice (pre/post) -->
          <template v-else-if="a.status === 'Confirmed'">
            <Button :label="t('timeAnomaly.direction.pre')" size="small" @click="apply(a, 'pre')" />
            <Button :label="t('timeAnomaly.direction.post')" size="small" @click="apply(a, 'post')" />
          </template>
          <!-- Applied → restore -->
          <Button v-else-if="a.status === 'Applied'"
            :label="t('timeAnomaly.restore')" size="small" @click="restore(a)" />
          <!-- Suspicious/Drift → ignore -->
          <Button v-else-if="a.status === 'Suspicious' || a.status === 'Drift'"
            :label="t('timeAnomaly.ignore')" size="small" @click="ignore(a)" />
          <Button v-if="a.status === 'Confirmed' || a.status === 'Pending'"
            :label="t('timeAnomaly.reviewHistory')" size="small" severity="secondary" @click="reviewInHistory(a)" />
        </div>
      </div>
      <button v-if="activeTotal" type="button" class="manual-correction-link" @click="startManualCorrection">
        <MousePointer2 :size="16" /> {{ t('timeAnomaly.manualAction') }}
      </button>
      <div v-if="archivedTotal" class="archive-section">
        <button type="button" class="archive-toggle" :aria-expanded="showHistory" @click="showHistory = !showHistory">
          <ChevronDown :size="17" :class="{ open: showHistory }" />
          {{ t('timeAnomaly.historyRecords', { count: archivedTotal }) }}
        </button>
        <Transition name="archive-expand">
          <div v-if="showHistory" class="archive-list">
            <article v-for="a in archivedItems" :key="a.id" class="archive-row">
              <span class="badge" :class="'badge-' + a.status">{{ t('timeAnomaly.status.' + a.status) }}</span>
              <span>{{ sourceLabel(a.source) }}</span>
              <span>{{ formatOffset(a.offsetSeconds) }}</span>
              <time>{{ formatTime(a.detectedAt) }}</time>
              <small v-if="a.note">{{ a.note }}</small>
              <Button v-if="a.status === 'Applied'" :label="t('timeAnomaly.restore')" size="small" @click="restore(a)" />
            </article>
          </div>
        </Transition>
      </div>
    </section>

    <!-- Preview confirmation dialog -->
    <Dialog v-model:visible="showPreview" :header="t('timeAnomaly.applyConfirm')" modal>
      <p class="preview-text">{{ t('timeAnomaly.applyPreview', {
        total: preview.total, tables: preview.tableNames.join(listSep) }) }}</p>
      <div class="dialog-actions">
        <Button :label="t('timeAnomaly.applyConfirm')" @click="confirmApply" />
        <Button :label="t('common.cancel')" severity="secondary" text @click="showPreview = false" />
      </div>
    </Dialog>
  </div>
</template>

<script setup>
import { ref, computed, inject, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from '../i18n/index.js'
import { parseUtcTs, toLocalDatetimeString } from '../utils/time.js'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import { ArrowRight, ChevronDown, CircleCheck, MousePointer2, TriangleAlert } from '@lucide/vue'

defineProps({ embedded: { type: Boolean, default: false } })

const apiBase = inject('apiBase')
const { t, locale } = useI18n()
const router = useRouter()

const items = ref([])
const ntp = ref({})
const checking = ref(false)
const showPreview = ref(false)
const preview = ref({ total: 0, tableNames: [] })
const pendingApply = ref(null)
const showHistory = ref(false)
const activeTotal = ref(0)
const archivedTotal = ref(0)

// Locale-aware list separator for the preview text
const listSep = computed(() => (locale.value === 'zh-CN' ? '、' : ', '))
const activeStatuses = new Set(['Confirmed', 'Pending', 'Suspicious', 'Drift'])
const activeItems = computed(() => items.value.filter(item => activeStatuses.has(item.status)))
const archivedItems = computed(() => items.value.filter(item => !activeStatuses.has(item.status)))

async function load() {
  try {
    const [activeResponse, archivedResponse] = await Promise.all([
      fetch(`${apiBase}/api/time-anomalies?limit=500&scope=active`),
      fetch(`${apiBase}/api/time-anomalies?limit=500&scope=archived`),
    ])
    if (!activeResponse.ok || !archivedResponse.ok) return
    const [activeJson, archivedJson] = await Promise.all([
      activeResponse.json(), archivedResponse.json(),
    ])
    const active = activeJson.items || []
    const archived = archivedJson.items || []
    items.value = [...active, ...archived]
    activeTotal.value = Number(activeJson.total ?? activeJson.activeCount ?? active.length)
    archivedTotal.value = Number(archivedJson.total ?? archivedJson.archivedCount ?? archived.length)
    ntp.value = activeJson.ntp || {}
  } catch (e) {
    console.error('load anomalies:', e)
  }
}
async function checkNtp() {
  checking.value = true
  try {
    await fetch(`${apiBase}/api/time-anomalies/ntp-check`, { method: 'POST' })
    await load()
  } finally {
    checking.value = false
  }
}
async function apply(a, direction) {
  const q = new URLSearchParams({ preview: 'true' })
  if (direction) q.set('direction', direction)
  const r = await fetch(`${apiBase}/api/time-anomalies/${a.id}/apply?${q}`, { method: 'POST' })
  if (!r.ok) return
  const j = await r.json()
  const counts = j.tableCounts || {}
  preview.value = {
    total: Object.values(counts).reduce((x, y) => x + y, 0),
    tableNames: Object.keys(counts),
  }
  pendingApply.value = { a, direction }
  showPreview.value = true
}
async function confirmApply() {
  const { a, direction } = pendingApply.value || {}
  if (!a) return
  const q = new URLSearchParams()
  if (direction) q.set('direction', direction)
  await fetch(`${apiBase}/api/time-anomalies/${a.id}/apply?${q}`, { method: 'POST' })
  showPreview.value = false
  await load()
}
async function restore(a) {
  await fetch(`${apiBase}/api/time-anomalies/${a.id}/restore`, { method: 'POST' })
  await load()
}
async function ignore(a) {
  await fetch(`${apiBase}/api/time-anomalies/${a.id}/ignore`, { method: 'POST' })
  await load()
}
function formatTime(v) {
  const d = v instanceof Date ? v : parseUtcTs(v)
  return d ? d.toLocaleString() : '—'
}
function formatNtpTime(v) {
  const d = parseUtcTs(v)
  return d ? d.toLocaleString() : t('timeAnomaly.ntpNever')
}
function formatOffset(value) {
  const seconds = Number(value || 0)
  return `${seconds > 0 ? '+' : ''}${seconds.toLocaleString()}s`
}
function shiftTime(value, seconds) {
  const date = value instanceof Date ? value : parseUtcTs(value)
  if (!date) return null
  return new Date(date.getTime() + seconds * 1000)
}
function formatRange(from, to) {
  const fromDate = from instanceof Date ? from : parseUtcTs(from)
  const toDate = to instanceof Date ? to : parseUtcTs(to)
  if (!fromDate && !toDate) return '—'
  if (!toDate || fromDate?.getTime() === toDate.getTime()) return fromDate?.toLocaleString() || toDate.toLocaleString()
  return `${fromDate?.toLocaleString() || '—'} — ${toDate.toLocaleString()}`
}
function timeChange(anomaly) {
  if (anomaly.oldTime || anomaly.newTime) {
    return {
      before: formatTime(anomaly.oldTime),
      after: formatTime(anomaly.newTime),
    }
  }

  const correction = -Number(anomaly.offsetSeconds || 0)
  const shiftedFrom = shiftTime(anomaly.fromWall, correction)
  const shiftedTo = shiftTime(anomaly.toWall, correction)
  return {
    before: formatRange(anomaly.fromWall, anomaly.toWall),
    after: formatRange(shiftedFrom, shiftedTo),
  }
}
function sourceLabel(source) {
  return source === 'User' ? t('timeAnomaly.sourceUser') : source
}
function startManualCorrection() {
  router.push({ path: '/history', query: { timeSelect: '1' } })
}
function reviewInHistory(anomaly) {
  const points = [anomaly.fromWall, anomaly.toWall, anomaly.oldTime, anomaly.newTime]
    .map(value => parseUtcTs(value)?.getTime())
    .filter(value => Number.isFinite(value))
  const anchor = points.length
    ? Math.min(...points)
    : parseUtcTs(anomaly.detectedAt)?.getTime() ?? Date.now()
  const sourceEnd = points.length > 1
    ? Math.max(...points)
    : anchor + Math.max(60, Math.abs(Number(anomaly.offsetSeconds) || 0)) * 1000
  const correction = -Number(anomaly.offsetSeconds || 0) * 1000
  const padding = Math.max(30 * 60 * 1000, Math.abs(correction) * .5)
  const from = new Date(Math.min(anchor, anchor + correction) - padding)
  const to = new Date(Math.max(sourceEnd, sourceEnd + correction) + padding)
  router.push({
    path: '/history',
    query: {
      timeSelect: '1',
      anomalyId: String(anomaly.id),
      from: toLocalDatetimeString(from),
      to: toLocalDatetimeString(to),
    },
  })
}
onMounted(load)
</script>

<style lang="scss" scoped>
.time-anomaly-page {
  width: 100%;
}

.time-anomaly-page.embedded .status-card { margin-top: 0; }

.page-title {
  font-size: 1.5rem;
  font-weight: 700;
  letter-spacing: 1px;
  margin-bottom: 24px;
  color: var(--text-color);
}

.section-title {
  font-size: 1rem;
  font-weight: 600;
  letter-spacing: 0.5px;
  margin-bottom: 16px;
  color: var(--text-color);
}

.memphis-box {
  background: var(--surface-card);
  border: 2px solid var(--surface-200);
  padding: 20px;
  transition: border-color 0.3s ease;

  &:hover {
    border-color: var(--primary-color);
  }
}

.list-card {
  margin-top: 24px;
  min-height: 200px;
}

.status-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 16px;
  margin-bottom: 16px;
}

.status-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 16px;
  border: 2px solid var(--surface-200);
  background: var(--surface-100);

  span {
    font-size: 0.85rem;
    color: var(--text-color-secondary);
  }

  b {
    font-size: 1.1rem;
    color: var(--text-color);
    word-break: break-all;
  }
}

.status-actions {
  display: flex;
  gap: 12px;
}

.current-time-state {
  margin-bottom: 16px;
  padding: 12px 14px;
  border: 2px solid var(--success-color);
  background: color-mix(in srgb, var(--success-color) 8%, var(--surface-card));
  color: var(--text-color);
  display: flex;
  align-items: center;
  gap: 10px;

  > svg { color: var(--success-color); flex: 0 0 auto; }
  > div { display: grid; gap: 3px; }
  b { font-size: .92rem; }
  span { color: var(--text-color-secondary); font-size: .78rem; }
  &.unavailable { border-color: var(--surface-300); background: var(--surface-100); }
  &.unavailable > svg { color: var(--surface-400); }
  &.problem { border-color: var(--warning-color); background: color-mix(in srgb, var(--warning-color) 9%, var(--surface-card)); }
  &.problem > svg { color: var(--warning-color); }
}

.anomaly-row {
  display: grid;
  gap: 14px;
  padding: 16px;
  border: 2px solid var(--surface-200);
  margin-bottom: 12px;
  background: var(--surface-card);
  transition: border-color 0.2s ease, transform 0.2s ease;

  &:hover {
    border-color: var(--primary-color);
    transform: translateY(-1px);
  }

  .badge {
    padding: 2px 10px;
    border: 2px solid var(--surface-300);
    font-size: 0.8rem;
    font-weight: 700;
    letter-spacing: 0.5px;
    color: var(--text-color);

    &.badge-Suspicious { border-color: var(--warning-color); color: var(--warning-color); }
    &.badge-Drift { border-color: var(--warning-color); color: var(--warning-color); }
    &.badge-Confirmed { border-color: var(--danger-color); color: var(--danger-color); }
    &.badge-Pending { border-color: var(--secondary-color); color: var(--secondary-color); }
    &.badge-Applied { border-color: var(--success-color); color: var(--success-color); }
    &.badge-Reverted { border-color: var(--surface-400); color: var(--surface-400); }
    &.badge-Ignored { border-color: var(--surface-400); color: var(--surface-400); }
    &.badge-Resolved { border-color: var(--surface-400); color: var(--surface-400); }
  }

  .note {
    font-size: 0.85rem;
    color: var(--surface-400);
  }

  .actions {
    display: flex;
    gap: 8px;
    flex-wrap: wrap;
    justify-content: flex-end;
  }
}

.anomaly-meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  color: var(--surface-400);
  font-size: 0.82rem;
}

.archive-section { margin-top: 16px; padding-top: 12px; border-top: 1px solid var(--surface-200); }
.archive-toggle {
  width: 100%; min-height: 38px; padding: 7px 9px;
  border: 1px solid var(--surface-300); background: transparent; color: var(--text-color);
  font: inherit; font-weight: 700; display: flex; align-items: center; gap: 7px; cursor: pointer;
}
.archive-toggle svg { transition: transform 160ms ease; }
.archive-toggle svg.open { transform: rotate(180deg); }
.archive-list { display: grid; margin-top: 8px; border: 1px solid var(--surface-200); }
.archive-row {
  min-width: 0; padding: 9px 10px; border-bottom: 1px solid var(--surface-200);
  display: grid; grid-template-columns: auto auto auto minmax(150px, 1fr) auto;
  align-items: center; gap: 8px; color: var(--text-color-secondary); font-size: .78rem;
}
.archive-row:last-child { border-bottom: 0; }
.archive-row small { grid-column: 1 / -1; overflow-wrap: anywhere; }
.archive-row .badge { padding: 2px 7px; border: 1px solid var(--surface-300); font-weight: 700; color: var(--surface-400); }
.archive-row .badge-Applied { border-color: var(--success-color); color: var(--success-color); }
.archive-row button { justify-self: end; }
.archive-expand-enter-active, .archive-expand-leave-active { transition: opacity 150ms ease, transform 180ms ease; transform-origin: top; }
.archive-expand-enter-from, .archive-expand-leave-to { opacity: 0; transform: scaleY(.94) translateY(-4px); }

.time-change {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: stretch;
  gap: 12px;
}

.time-point {
  min-width: 0;
  padding: 12px 14px;
  border: 2px solid var(--surface-200);
  background: var(--surface-100);
  display: grid;
  gap: 5px;

  span {
    color: var(--surface-400);
    font-size: 0.78rem;
    font-weight: 700;
  }

  b {
    color: var(--text-color);
    font-size: 0.92rem;
    line-height: 1.45;
    overflow-wrap: anywhere;
  }

  &.after {
    border-color: color-mix(in srgb, var(--primary-color) 55%, var(--surface-200));
  }
}

.time-arrow {
  align-self: center;
  color: var(--primary-color);
}

.empty {
  text-align: center;
  padding: 32px;
  color: var(--surface-400);
  font-style: italic;
}

.empty-action {
  width: 100%;
  border: 2px dashed var(--surface-300);
  background: var(--surface-100);
  cursor: pointer;
  display: grid;
  justify-items: center;
  gap: 6px;
  color: var(--success-color);

  span { color: var(--text-color-secondary); font-style: normal; }
  &:hover { border-color: var(--primary-color); }
}

.manual-correction-link {
  min-height: 38px;
  margin-top: 4px;
  padding: 7px 10px;
  border: 2px solid var(--primary-color);
  background: transparent;
  color: var(--text-color);
  font-weight: 600;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 7px;
}

.preview-text {
  margin-bottom: 20px;
  color: var(--text-color);
  line-height: 1.6;
}

.dialog-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
}

@media (max-width: 700px) {
  .time-change {
    grid-template-columns: 1fr;
  }

  .time-arrow {
    transform: rotate(90deg);
    justify-self: center;
  }

  .archive-row { grid-template-columns: auto 1fr; }
  .archive-row time, .archive-row small { grid-column: 1 / -1; }
}
</style>
