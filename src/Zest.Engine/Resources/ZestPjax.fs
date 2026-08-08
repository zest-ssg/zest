// ZestPjax.fs
//
// Self-contained pjax script for Zest themes.
// Embeds the JavaScript as a string constant for inline injection.
//
// Dependencies: none
namespace Zest.Engine.Resources

/// <summary>
/// Self-contained pjax script for Zest themes.
/// Embeds the JavaScript as a string constant for inline injection.
/// </summary>
module ZestPjax =

    /// <summary>
    /// Pjax client script (single source of truth).
    /// Usage: inject via {{ pjaxScript | safe }} in templates or pjax_script in DSL.
    /// Features: same-origin link interception (modifier keys, non-nav targets),
    /// in-memory cache + hover prefetch, concurrency lock + 8s AbortController
    /// timeout, hash-aware navigation with scroll restoration, popstate support,
    /// prefers-reduced-motion support.
    /// </summary>
    let script = """<script>
(function(){
'use strict';
if(window.__zestPjaxLoaded)return;window.__zestPjaxLoaded=true;
var rM=!!(window.matchMedia&&window.matchMedia('(prefers-reduced-motion: reduce)').matches);
var c=document.querySelector('main')||document.getElementById('content')||document.body;
var loading=false,hist={},store={},MAX=24,pt=null;
function C(){if(!document.contains(c))c=document.querySelector('main')||document.getElementById('content')||document.body;return c}
function K(h){var u=new URL(h,location.href);return u.origin+u.pathname+u.search}
function S(h){return K(h)===K(location.href)}
function G(u){return Object.prototype.hasOwnProperty.call(store,u)?store[u]:null}
function P(u,t){store[u]=t;var k=Object.keys(store);while(k.length>MAX)delete store[k.shift()]}
function F(u,s){var h=G(u);if(h)return Promise.resolve(h);
return fetch(u,{headers:{'X-PJAX':'true'},cache:'no-store',credentials:'same-origin',signal:s}).then(function(r){if(!r.ok)throw Error('HTTP '+r.status);return r.text()}).then(function(t){P(u,t);return t})}
function H(h){if(h&&h.charAt(0)==='#'){var id;try{id=decodeURIComponent(h.slice(1))}catch(e){id=h.slice(1)}var el=document.getElementById(id);if(el){el.scrollIntoView();return}}window.scrollTo(0,0)}
function R(h,fh){var u=new URL(h,location.href);if(u.hash){H(u.hash);return}var k=K(h);if(fh&&Object.prototype.hasOwnProperty.call(hist,k))window.scrollTo(0,hist[k]);else window.scrollTo(0,0)}
function D(){if(rM)return;var x=C();x.style.transition='opacity .15s ease';x.style.opacity='0.3'}
function U(){if(rM)return;C().style.opacity='1'}
function load(h,o){o=o||{};var push=o.push!==false;var u=new URL(h,location.href);var uh=u.href;
if(u.origin!==location.origin)return;
if(S(uh)){H(u.hash);return}
if(loading)return;loading=true;
if(push)hist[K(location.href)]=window.pageYOffset||document.documentElement.scrollTop||0;
D();
var ab=typeof AbortController!=='undefined'?new AbortController():null;
var to=ab?setTimeout(function(){ab.abort()},8000):null;
F(uh,ab?ab.signal:undefined).then(function(t){var d=new DOMParser().parseFromString(t,'text/html');var n=d.querySelector('main')||d.getElementById('content')||d.body;var x=C();x.innerHTML=n.innerHTML;if(d.title)document.title=d.title;if(push)history.pushState({pjax:true,url:uh},'',uh);U();R(uh,!push);document.dispatchEvent(new CustomEvent('pjax:end',{detail:{url:uh}}))}).catch(function(e){U();if(e&&e.name==='AbortError'&&window.console&&console.warn)console.warn('[pjax] request timed out, falling back to full navigation');if(push)location.href=uh}).then(function(){loading=false;if(to)clearTimeout(to)})}
function A(a){var r=a.getAttribute('href');if(!r||r.charAt(0)==='#')return false;
if(r.indexOf('javascript:')===0||r.indexOf('mailto:')===0||r.indexOf('tel:')===0)return false;
if(a.host!==location.host)return false;
if(a.hasAttribute('download'))return false;
if(a.getAttribute('target')==='_blank')return false;
return true}
document.addEventListener('click',function(e){var t=e.target;if(!(t instanceof Element))return;if(e.defaultPrevented||e.button!==0)return;if(e.metaKey||e.ctrlKey||e.shiftKey||e.altKey)return;var a=t.closest('a[href]');if(!a||!A(a))return;e.preventDefault();load(a.href,{push:true})},true);
document.addEventListener('mouseover',function(e){var t=e.target;if(!(t instanceof Element))return;var a=t.closest('a[href]');if(!a||!A(a))return;var u=new URL(a.href,location.href);if(u.origin!==location.origin||S(u.href)||G(u.href))return;if(pt)clearTimeout(pt);pt=setTimeout(function(){F(u.href).catch(function(){})},150)},true);
window.addEventListener('popstate',function(e){if(e.state&&e.state.pjax&&e.state.url)load(e.state.url,{push:false})});
})();
</script>"""
