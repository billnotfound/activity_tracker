// Shared ECharts setup — register only the chart types/components both views use,
// so the bundle excludes ~550 KB of unused chart types (pie/line/scatter/etc.).
//
// Usage in views:
//   import { echarts } from '../utils/echartsInit.js'
//   const chart = echarts.init(domRef.value)
//   chart.setOption({...})
import * as echarts from 'echarts/core'
import { BarChart, CustomChart } from 'echarts/charts'
import {
  GridComponent,
  TooltipComponent,
  AxisPointerComponent,
} from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'

echarts.use([
  BarChart,
  CustomChart,
  GridComponent,
  TooltipComponent,
  AxisPointerComponent,
  CanvasRenderer,
])

export { echarts }
