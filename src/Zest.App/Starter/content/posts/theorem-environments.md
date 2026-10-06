+++
title = "Theorem environments and display equations"
layout = "post"
date = 2026-06-30
tags = ["latex", "layout"]
description = "Numbered theorems, remarks, figures and display equations, written as plain HTML inside a post."
+++

Some pages need more than paragraphs. When a post has to make a claim and defend it, reach for the three structures below: a theorem box, a numbered display equation, and a figure. All three are plain HTML that the renderer passes through untouched, so nothing has to be enabled first.

## Theorem environments

Wrap a claim in `div.theorem` and give it a bold label. The body stays italic, which is how a paper sets a statement apart from the prose around it:

<div class="theorem">
<p><span class="theorem__label">Theorem 1.</span> <em>A site that ships no JavaScript cannot fail to hydrate.</em></p>
</div>

The same box carries a remark or a definition with one extra class, so the reader keeps a single visual language for everything that is not body text:

<div class="theorem theorem--plain">
<p><span class="theorem__label">Remark 1.</span> <em>Numbering is the navigation. If a reader can point at a claim, they can cite it.</em></p>
</div>

## Display equations

An equation gets its own centred line with a tag flushed right, exactly like a paper. Italic serif stands in for mathematical type; superscripts and subscripts come from ordinary HTML:

<div class="equation">
<span class="equation__body"><em>t</em><sub>build</sub> = <em>n</em> &middot; <em>t</em><sub>page</sub> + <em>t</em><sub>link</sub></span>
<span class="equation__tag">(1)</span>
</div>

A second statement continues the count, so the reader can refer back to (1) in the prose:

<div class="equation">
<span class="equation__body"><em>t</em><sub>link</sub> &rarr; 0 as <em>n</em> grows</span>
<span class="equation__tag">(2)</span>
</div>

## Figures

A figure is content plus a centred caption underneath, in the smaller size. There is no decorative imagery anywhere in this design, so a figure has to carry data: this one plots the claim in (2).

<figure class="figure">
<svg class="figure__plot" viewBox="0 0 320 150" role="img" aria-label="Link time falls as the number of pages grows">
<line x1="40" y1="14" x2="40" y2="118" stroke="#111111" stroke-width="1"></line>
<line x1="40" y1="118" x2="300" y2="118" stroke="#111111" stroke-width="1"></line>
<polyline points="40,26 92,44 144,68 196,86 248,98 296,106" fill="none" stroke="#0B5394" stroke-width="2"></polyline>
<text x="46" y="22" class="figure__axis">t_link</text>
<text x="272" y="134" class="figure__axis">n</text>
</svg>
<figcaption class="figure__caption">Figure 1: Link time against the number of pages.</figcaption>
</figure>

## Footnotes

A footnote is a superscript link in hyperref blue, paired with an entry in the rule-separated block at the foot of the post. Cite it inline like this<sup class="footnote-ref"><a href="#fn-1" id="fnref-1">1</a></sup> and keep the block out of the section numbering.

<div class="footnotes">
<hr class="footnotes__rule">
<ol class="footnotes__list">
<li id="fn-1">Numbering every element is the whole trick: nothing has to be remembered, only looked up.</li>
</ol>
</div>
