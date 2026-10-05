# Enable the hooks in this directory (Windows equivalent of install.sh).
#
# Husky's own installer is a Node package; this repository has no Node
# dependency, so the hooks are wired up the way Git supports natively: point
# core.hooksPath at this folder. Run once per clone.
#
#   pwsh .husky/install.ps1

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

git config core.hooksPath .husky

$hooksPath = git config --get core.hooksPath
Write-Host "[husky] core.hooksPath set to $hooksPath"
