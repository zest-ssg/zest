+++
title = "Welcome to Zest"
layout = "post"
description = "A short introduction to the Zest static site generator."
date = 2026-01-18
tags = ["fsharp", "ssg"]
+++

Zest is a hybrid F# + C# static site generator where templates are
real code. This starter blog was created by running `zest init` and
demonstrates the native template mode.

## What makes Zest different

Pages are `.zest.fsx` scripts — ordinary F# files evaluated by the
build. You can use the full language: list comprehensions, pattern
matching, .NET libraries, and your own helpers.

Layouts are written in HTML and processed by the Nunjucks engine,
so you get includes, variables, and filters with no extra setup.

## Next steps

- Read the [Building Sites with F#](/posts/building-sites-with-fsharp/) guide
- Explore the [Features](/features/) demo page
- Check the [Zest repository](https://github.com/zest-ssg/zest)
