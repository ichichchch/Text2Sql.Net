// Lightweight ECharts interop for Blazor
// Exposes: renderAutoChart(containerId, dataRows), dispose(containerId), exportPng(containerId)

window.echartsInterop = (function () {
  const instances = new Map();

  function getContainer(containerId) {
    const el = document.getElementById(containerId);
    if (!el) throw new Error(`container not found: ${containerId}`);
    return el;
  }

  function ensureInstance(containerId) {
    const el = getContainer(containerId);
    let chart = instances.get(containerId);
    if (!chart) {
      chart = echarts.init(el, undefined, { renderer: 'canvas' });
      instances.set(containerId, chart);
      window.addEventListener('resize', () => {
        try { chart.resize(); } catch (_) { }
      });
    }
    return chart;
  }

  function inferMeta(dataRows) {
    // dataRows: Array<Dictionary<string, object>> from .NET → becomes array of objects
    if (!Array.isArray(dataRows) || dataRows.length === 0) {
      return { type: 'empty' };
    }
    const row = dataRows[0];
    const keys = Object.keys(row);
    // Choose first non-numeric column as category; the rest numeric as metrics
    let categoryKey = keys.find(k => typeof row[k] === 'string');
    if (!categoryKey) categoryKey = keys[0];
    const numberKeys = keys.filter(k => typeof row[k] === 'number' || (!isNaN(Number(row[k])) && row[k] !== null && row[k] !== ''));
    const numericMetrics = numberKeys.filter(k => k !== categoryKey);

    // If only one numeric metric → bar; if many rows>30 → line; if two columns only and both numeric → scatter
    if (keys.length === 2 && numberKeys.length === 2) {
      return { type: 'scatter', xKey: keys[0], yKey: keys[1] };
    }
    if (numericMetrics.length === 0) {
      // try to parse a numeric column anyway
      const fallback = keys.find(k => k !== categoryKey);
      if (fallback) numericMetrics.push(fallback);
    }
    const manyRows = dataRows.length > 30;
    const chartType = manyRows ? 'line' : 'bar';
    return { type: chartType, categoryKey, valueKeys: numericMetrics.length ? numericMetrics : [keys[1] || keys[0]] };
  }

  function toOption(meta, dataRows) {
    if (meta.type === 'empty') {
      return { title: { text: '无数据' } };
    }
    if (meta.type === 'scatter') {
      return {
        tooltip: { trigger: 'item' },
        xAxis: { type: 'value', name: meta.xKey },
        yAxis: { type: 'value', name: meta.yKey },
        series: [{
          type: 'scatter',
          data: dataRows.map(r => [Number(r[meta.xKey]), Number(r[meta.yKey])])
        }]
      };
    }

    const categories = dataRows.map(r => String(r[meta.categoryKey] ?? ''));
    const series = (meta.valueKeys || []).map(k => ({
      name: k,
      type: meta.type,
      data: dataRows.map(r => Number(r[k]))
    }));

    return {
      tooltip: { trigger: 'axis' },
      legend: { type: 'scroll' },
      grid: { left: 40, right: 20, top: 40, bottom: 40 },
      xAxis: { type: 'category', data: categories, axisLabel: { interval: 'auto' } },
      yAxis: { type: 'value' },
      series
    };
  }

  function renderAutoChart(containerId, dataRows) {
    const chart = ensureInstance(containerId);
    const meta = inferMeta(dataRows);
    const option = toOption(meta, dataRows);
    chart.setOption(option, true);
  }

  function dispose(containerId) {
    const chart = instances.get(containerId);
    if (chart) {
      try { chart.dispose(); } catch (_) { }
      instances.delete(containerId);
    }
  }

  function exportPng(containerId) {
    const chart = instances.get(containerId);
    if (!chart) return;
    const url = chart.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#fff' });
    const a = document.createElement('a');
    a.href = url;
    a.download = 'chart.png';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
  }

  return { renderAutoChart, dispose, exportPng };
})();


