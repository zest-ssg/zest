// main.js — Zest Docs 站点交互脚本
// 由 Zest 构建时原样复制到 _site/assets/js/
// 配合 .zpage.fsx + .zcss 生态使用。

document.addEventListener('DOMContentLoaded', () => {
  // 代码高亮（简单类名标记）
  document.querySelectorAll('pre code').forEach(block => {
    block.classList.add('highlighted');
  });

  // 当前导航项激活
  const currentPath = location.pathname;
  document.querySelectorAll('.site-nav a').forEach(link => {
    if (link.getAttribute('href') === currentPath) {
      link.classList.add('active');
    }
  });
});
