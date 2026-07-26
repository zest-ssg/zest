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
    /// Minified pjax client script.
    /// Usage: inject via {{ pjaxScript | safe }} in templates or pjax_script in DSL.
    /// </summary>
    let script = """<script>
(function(){
function c(){return document.querySelector('main')||document.getElementById('content')||document.body}
function load(href,push){if(!push)push=true;
document.dispatchEvent(new CustomEvent('pjax:start',{detail:{url:href}}));
var container=c();
container.style.opacity='0.3';
container.style.transition='opacity .15s';
fetch(href,{headers:{'X-PJAX':'true'}}).then(function(r){if(!r.ok)throw Error(r.status);
return r.text()}).then(function(html){var doc=new DOMParser().parseFromString(html,'text/html');
var next=doc.querySelector('main')||doc.getElementById('content')||doc.body;
var container=c();
if(next)container.innerHTML=next.innerHTML;
if(push)history.pushState({pjax:true,url:href},'',href);
document.title=doc.title;
container.style.opacity='1';
document.dispatchEvent(new CustomEvent('pjax:end',{detail:{url:href}}));
}).catch(function(e){var container=c();container.style.opacity='1';
if(push)location.href=href})}
document.addEventListener('click',function(e){var a=e.target.closest('a[href]');
if(!a)return;var href=a.getAttribute('href');
if(!href||href.startsWith('#')||href.startsWith('javascript:')||href.startsWith('mailto:')||href.startsWith('tel:'))return;
if(a.host!==location.host)return;if(a.hasAttribute('download'))return;
if(a.getAttribute('target')==='_blank')return;
e.preventDefault();load(href,true)});
window.addEventListener('popstate',function(e){if(e.state&&e.state.pjax)
load(e.state.url||location.href,false)})
})();
</script>"""
