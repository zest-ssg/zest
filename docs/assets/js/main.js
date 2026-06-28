// main.js — minimal nav highlight
document.addEventListener('DOMContentLoaded', () => {
  const p = location.pathname.replace(/\/$/, '') || '/';
  document.querySelectorAll('.nav-links a').forEach(a => {
    const h = a.getAttribute('href')?.replace(/\/$/, '') || '';
    if (h === p || (h !== '/' && p.startsWith(h))) a.style.fontWeight = '700';
  });
});
