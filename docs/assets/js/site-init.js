// site-init.js
// 演示 .js 与 .zest.fsx + .zss 的生态配合
// 由 Zest 构建时原样复制到 _site/assets/js/

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
