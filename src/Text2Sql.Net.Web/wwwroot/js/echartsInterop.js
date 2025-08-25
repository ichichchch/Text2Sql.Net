// Enhanced ECharts interop for Blazor with column control and chart type switching
// Exposes: renderAutoChart, renderCustomChart, dispose, exportPng, getAvailableColumns, getChartTypes

window.echartsInterop = (function () {
  const instances = new Map();
  const chartData = new Map(); // Store original data for column switching

  function getContainer(containerId) {
    const el = document.getElementById(containerId);
    if (!el) throw new Error(`container not found: ${containerId}`);
    return el;
  }

  function ensureInstance(containerId) {
    const el = getContainer(containerId);
    let chart = instances.get(containerId);
    if (!chart) {
      chart = echarts.init(el, undefined, { 
        renderer: 'canvas',
        width: 'auto',
        height: 'auto'
      });
      instances.set(containerId, chart);
      
      // 窗口大小变化时调整图表
      const resizeHandler = () => {
        try { 
          setTimeout(() => chart.resize(), 100);
        } catch (_) { }
      };
      
      window.addEventListener('resize', resizeHandler);
      
      // 也监听容器大小变化（如果支持ResizeObserver）
      if (window.ResizeObserver) {
        const resizeObserver = new ResizeObserver(resizeHandler);
        resizeObserver.observe(el);
      }
    }
    return chart;
  }

  function getAvailableColumns(dataRows) {
    if (!Array.isArray(dataRows) || dataRows.length === 0) {
      return { categoryColumns: [], valueColumns: [] };
    }
    
    const row = dataRows[0];
    const keys = Object.keys(row);
    const categoryColumns = [];
    const valueColumns = [];
    
    keys.forEach(key => {
      const value = row[key];
      if (typeof value === 'string' || value instanceof Date) {
        categoryColumns.push({ key, label: key, type: 'category' });
      } else if (typeof value === 'number' || (!isNaN(Number(value)) && value !== null && value !== '')) {
        valueColumns.push({ key, label: key, type: 'value' });
      } else {
        categoryColumns.push({ key, label: key, type: 'category' });
      }
    });
    
    return { categoryColumns, valueColumns, allColumns: keys };
  }

  function getChartTypes() {
    return [
      { key: 'bar', label: '柱状图', icon: 'bar-chart' },
      { key: 'line', label: '折线图', icon: 'line-chart' },
      { key: 'pie', label: '饼图', icon: 'pie-chart' },
      { key: 'scatter', label: '散点图', icon: 'dot-chart' },
      { key: 'area', label: '面积图', icon: 'area-chart' }
    ];
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
        tooltip: { 
          trigger: 'item',
          formatter: function(params) {
            return `${meta.xKey}: ${params.data[0]}<br/>${meta.yKey}: ${params.data[1]}`;
          }
        },
        xAxis: { type: 'value', name: meta.xKey },
        yAxis: { type: 'value', name: meta.yKey },
        series: [{
          type: 'scatter',
          data: dataRows.map(r => [Number(r[meta.xKey]), Number(r[meta.yKey])]),
          symbolSize: 8
        }]
      };
    }
    
    if (meta.type === 'pie') {
      const categoryKey = meta.categoryKey || Object.keys(dataRows[0])[0];
      const valueKey = meta.valueKeys?.[0] || Object.keys(dataRows[0])[1];
      
      return {
        tooltip: { 
          trigger: 'item',
          formatter: '{a} <br/>{b}: {c} ({d}%)'
        },
        legend: {
          type: 'scroll',
          orient: 'vertical',
          right: 10,
          top: 20,
          bottom: 20
        },
        series: [{
          name: valueKey,
          type: 'pie',
          radius: ['40%', '70%'],
          avoidLabelOverlap: false,
          data: dataRows.map(r => ({
            name: String(r[categoryKey] ?? ''),
            value: Number(r[valueKey])
          }))
        }]
      };
    }

    const categories = dataRows.map(r => String(r[meta.categoryKey] ?? ''));
    const series = (meta.valueKeys || []).map(k => ({
      name: k,
      type: meta.type === 'area' ? 'line' : meta.type,
      data: dataRows.map(r => Number(r[k])),
      areaStyle: meta.type === 'area' ? {} : undefined,
      smooth: meta.type === 'line' || meta.type === 'area'
    }));

    return {
      tooltip: { 
        trigger: 'axis',
        axisPointer: {
          type: meta.type === 'line' || meta.type === 'area' ? 'cross' : 'shadow'
        }
      },
      legend: { 
        type: 'scroll',
        top: 10
      },
      grid: { 
        left: 50, 
        right: 30, 
        top: 50, 
        bottom: 50,
        containLabel: true
      },
      xAxis: { 
        type: 'category', 
        data: categories, 
        axisLabel: { 
          interval: 'auto',
          rotate: categories.length > 10 ? 45 : 0
        }
      },
      yAxis: { type: 'value' },
      series
    };
  }

  function renderAutoChart(containerId, dataRows) {
    const chart = ensureInstance(containerId);
    const meta = inferMeta(dataRows);
    const option = toOption(meta, dataRows);
    chart.setOption(option, true);
    
    // 强制调整图表尺寸
    setTimeout(() => {
      try {
        chart.resize();
      } catch (_) { }
    }, 200);
    
    // Store data for future customization
    chartData.set(containerId, dataRows);
  }

  function renderCustomChart(containerId, dataRows, chartType, categoryColumn, valueColumns) {
    const chart = ensureInstance(containerId);
    
    // Store data for future use
    chartData.set(containerId, dataRows);
    
    if (!Array.isArray(dataRows) || dataRows.length === 0) {
      const option = { title: { text: '无数据' } };
      chart.setOption(option, true);
      return;
    }
    
    const meta = {
      type: chartType,
      categoryKey: categoryColumn,
      valueKeys: Array.isArray(valueColumns) ? valueColumns : [valueColumns]
    };
    
    // For scatter plot, use first two value columns as x and y
    if (chartType === 'scatter' && meta.valueKeys.length >= 2) {
      meta.xKey = meta.valueKeys[0];
      meta.yKey = meta.valueKeys[1];
    }
    
    const option = toOption(meta, dataRows);
    chart.setOption(option, true);
    
    // 强制调整图表尺寸
    setTimeout(() => {
      try {
        chart.resize();
      } catch (_) { }
    }, 200);
  }

  function updateChartType(containerId, chartType) {
    const data = chartData.get(containerId);
    if (!data) return;
    
    const chart = instances.get(containerId);
    if (!chart) return;
    
    const meta = inferMeta(data);
    meta.type = chartType;
    
    const option = toOption(meta, data);
    chart.setOption(option, true);
  }

  function updateChartColumns(containerId, categoryColumn, valueColumns) {
    const data = chartData.get(containerId);
    if (!data) return;
    
    const chart = instances.get(containerId);
    if (!chart) return;
    
    const meta = {
      type: 'bar', // Default type, can be changed later
      categoryKey: categoryColumn,
      valueKeys: Array.isArray(valueColumns) ? valueColumns : [valueColumns]
    };
    
    const option = toOption(meta, data);
    chart.setOption(option, true);
  }

  function dispose(containerId) {
    const chart = instances.get(containerId);
    if (chart) {
      try { chart.dispose(); } catch (_) { }
      instances.delete(containerId);
    }
    chartData.delete(containerId);
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

  // Public API
  return {
    renderAutoChart,
    renderCustomChart,
    updateChartType,
    updateChartColumns,
    getAvailableColumns,
    getChartTypes,
    dispose,
    exportPng
  };
})();


