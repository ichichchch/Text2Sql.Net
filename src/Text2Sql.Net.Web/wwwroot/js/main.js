// 数据库聊天页面相关功能
window.databaseChatFunctions = {
    // 滚动聊天容器到底部
    scrollChatToBottom: function (element) {
        if (!element) return;
        
        try {
            if (typeof element.scrollTo === 'function') {
                element.scrollTo({
                    top: element.scrollHeight,
                    behavior: 'smooth'
                });
            } else {
                element.scrollTop = element.scrollHeight;
            }
        } catch (e) {
            console.error('滚动失败:', e);
        }
    },
    
    // 复制文本到剪贴板
    copyToClipboard: function (text) {
        if (!text) return Promise.reject('无文本可复制');
        
        return navigator.clipboard.writeText(text)
            .then(() => true)
            .catch(err => {
                console.error('复制失败:', err);
                // 回退方案
                try {
                    const textArea = document.createElement('textarea');
                    textArea.value = text;
                    document.body.appendChild(textArea);
                    textArea.focus();
                    textArea.select();
                    const successful = document.execCommand('copy');
                    document.body.removeChild(textArea);
                    return successful;
                } catch (e) {
                    console.error('回退复制失败:', e);
                    return false;
                }
            });
    },
    
    // SQL语法高亮功能
    highlightSql: function(element) {
        if (!element) return;
        
        try {
            // 确保Prism.js已加载
            if (typeof Prism !== 'undefined') {
                // 为元素添加SQL语法高亮类
                element.classList.add('language-sql');
                
                // 应用语法高亮
                Prism.highlightElement(element);
                
                // 增强关键词高亮
                this.enhanceKeywordHighlight(element);
            } else {
                console.warn('Prism.js未加载，无法进行语法高亮');
            }
        } catch (e) {
            console.error('SQL语法高亮失败:', e);
        }
    },

    // 增强关键词高亮
    enhanceKeywordHighlight: function(element) {
        try {
            const primaryKeywords = ['select', 'from', 'where', 'insert', 'update', 'delete', 
                                   'create', 'alter', 'drop', 'join', 'inner', 'left', 'right', 'full'];
            
            const keywordTokens = element.querySelectorAll('.token.keyword');
            keywordTokens.forEach(token => {
                const text = token.textContent.toLowerCase().trim();
                if (primaryKeywords.includes(text)) {
                    token.classList.add('sql-primary');
                }
            });
        } catch (e) {
            console.error('增强关键词高亮失败:', e);
        }
    },
    
    // 高亮所有SQL代码块
    highlightAllSql: function() {
        console.log('开始高亮所有SQL代码块...');
        try {
            if (typeof Prism !== 'undefined') {
                console.log('Prism.js已加载');
                
                // 查找所有SQL相关的pre元素
                const allPreElements = document.querySelectorAll('pre');
                console.log(`找到 ${allPreElements.length} 个pre元素`);
                
                allPreElements.forEach((element, index) => {
                    const content = element.textContent.toLowerCase();
                    console.log(`处理第 ${index + 1} 个pre元素，内容: ${content.substring(0, 50)}...`);
                    
                    // 强制添加language-sql类
                    if (this.isSqlContent(content) || element.classList.contains('language-sql')) {
                        element.classList.add('language-sql');
                        console.log(`为元素 ${index + 1} 添加language-sql类`);
                        
                        // 先清理之前的高亮
                        element.innerHTML = element.textContent;
                        
                        // 尝试应用Prism高亮
                        try {
                            Prism.highlightElement(element);
                            console.log(`为元素 ${index + 1} 应用Prism高亮`);
                            
                            // 延迟应用增强高亮
                            setTimeout(() => {
                                this.enhanceKeywordHighlight(element);
                                console.log(`为元素 ${index + 1} 应用增强高亮`);
                            }, 100);
                        } catch (e) {
                            console.log(`Prism高亮失败，使用后备方案: ${e.message}`);
                            this.fallbackHighlight(element);
                        }
                    }
                });
                
                console.log('SQL高亮处理完成');
            } else {
                console.error('Prism.js未加载，使用后备高亮方案');
                // 如果Prism.js未加载，使用后备方案
                const allPreElements = document.querySelectorAll('pre');
                allPreElements.forEach((element, index) => {
                    const content = element.textContent.toLowerCase();
                    if (this.isSqlContent(content) || element.classList.contains('language-sql')) {
                        element.classList.add('language-sql');
                        this.fallbackHighlight(element);
                        console.log(`为元素 ${index + 1} 应用后备高亮`);
                    }
                });
            }
        } catch (e) {
            console.error('批量SQL语法高亮失败:', e);
        }
    },
    
    // 判断内容是否为SQL
    isSqlContent: function(content) {
        const sqlKeywords = ['select', 'from', 'where', 'insert', 'update', 'delete', 
                           'create', 'alter', 'drop', 'join', 'group by', 'order by',
                           'having', 'union', 'distinct', 'count', 'sum', 'avg', 'max', 'min'];
        
        return sqlKeywords.some(keyword => content.includes(keyword));
    },
    
    // 简单的后备高亮方案（不依赖Prism.js）
    fallbackHighlight: function(element) {
        try {
            const content = element.textContent;
            const keywords = ['SELECT', 'FROM', 'WHERE', 'INSERT', 'UPDATE', 'DELETE', 
                            'CREATE', 'ALTER', 'DROP', 'JOIN', 'INNER', 'LEFT', 'RIGHT', 'FULL',
                            'GROUP BY', 'ORDER BY', 'HAVING', 'UNION', 'DISTINCT'];
            
            let highlightedContent = content;
            
            // 将内容转换为大写进行匹配，但保持原始大小写用于显示
            keywords.forEach(keyword => {
                const regex = new RegExp(`\\b${keyword}\\b`, 'gi');
                highlightedContent = highlightedContent.replace(regex, 
                    `<span class="sql-keyword-fallback">${keyword}</span>`);
            });
            
            element.innerHTML = highlightedContent;
            console.log('应用后备高亮方案');
        } catch (e) {
            console.error('后备高亮失败:', e);
        }
    },

    // 确认函数已加载
    isLoaded: function() {
        return true;
    }
};

// 确保函数已准备就绪
console.log("数据库聊天功能已加载");

// 在Blazor启动后执行
document.addEventListener('DOMContentLoaded', function() {
    // 防止功能未加载情况下的错误
    if (!window.databaseChatFunctions) {
        console.error("警告：数据库聊天功能未正确加载，请检查main.js文件");
        // 提供基本功能以防止页面崩溃
        window.databaseChatFunctions = {
            scrollChatToBottom: function() { console.warn("滚动功能未加载"); },
            copyToClipboard: function() { console.warn("复制功能未加载"); return Promise.resolve(false); },
            isLoaded: function() { return false; }
        };
    }
});
