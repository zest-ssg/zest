<#
.SYNOPSIS
    Zest SSG — Build & Pack script
.DESCRIPTION
    Unified build/pack/test script for Zest SSG.
    Supports build, publish, clean, and test targets.
.PARAMETER Target
    Build target: Build (default), Publish, Clean, Test, Serve, Preview
.PARAMETER Configuration
    Build configuration: Debug (default) or Release
.PARAMETER OutputDir
    Publish output directory (default: ./publish)
.PARAMETER Port
    Dev server or preview server port (default: 8080)
.EXAMPLE
    .\build.ps1                    # Debug build
    .\build.ps1 Publish            # Release single-file publish
    .\build.ps1 Publish -Configuration Release -OutputDir ./dist
    .\build.ps1 Clean
    .\build.ps1 Test
    .\build.ps1 Serve -Port 8080
    .\build.ps1 Preview -Port 8080
#>

param(
    [ValidateSet('Build', 'Publish', 'Clean', 'Test', 'Serve', 'Preview')]
    [string]$Target = 'Build',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$OutputDir = './publish',

    [int]$Port = 8080
)

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Solution = Join-Path $Root 'Zest.sln'
$Project  = Join-Path $Root 'src\Zest.App\Zest.App.csproj'

# ── helpers ──────────────────────────────────────────────────────────
function Write-Step($msg) {
    Write-Host "`n━━━ $msg ━━━" -ForegroundColor Cyan
}

function Write-Error($msg) {
    Write-Host "ERROR: $msg" -ForegroundColor Red
}

function Write-Success($msg) {
    Write-Host "✓ $msg" -ForegroundColor Green
}

# ── targets ──────────────────────────────────────────────────────────
function Invoke-Build {
    Write-Step "Building $Configuration ..."
    $r = dotnet build $Solution -c $Configuration --nologo 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Build failed`n$r" }
    Write-Success "Build succeeded"
}

function Invoke-Publish {
    if ($Configuration -eq 'Debug') {
        Write-Host "  [i] Publish in Debug mode — output includes debug symbols" -ForegroundColor Yellow
    }

    Write-Step "Publishing $Configuration (single-file, self-contained) ..."
    $out = Join-Path $Root $OutputDir
    $null = New-Item -ItemType Directory -Force -Path $out

    $r = dotnet publish $Project -c $Configuration `
        --self-contained true `
        -r win-x64 `
        -o $out `
        --nologo 2>&1

    if ($LASTEXITCODE -ne 0) { throw "Publish failed`n$r" }

    $bin = Get-Item (Join-Path $out 'zest.exe') -ErrorAction SilentlyContinue
    if ($bin) {
        $size = [math]::Round($bin.Length / 1MB, 1)
        Write-Success "Published to: $out"
        Write-Host "  Binary: zest.exe ($size MB)"
    } else {
        Write-Success "Published to: $out"
    }
}

function Invoke-Clean {
    Write-Step "Cleaning ..."
    dotnet clean $Solution -c Debug   --nologo 2>&1 | Out-Null
    dotnet clean $Solution -c Release --nologo 2>&1 | Out-Null

    $dirs = @('bin', 'obj', 'publish', 'dist')
    foreach ($d in $dirs) {
        $path = Join-Path $Root $d
        if (Test-Path $path) {
            Remove-Item -Recurse -Force -Path $path -ErrorAction SilentlyContinue
            Write-Host "  Removed: $d"
        }
    }
    Write-Success "Clean complete"
}

function Invoke-Test {
    # Build docs site as integration test
    Write-Step "Building docs/ as integration test ..."
    Push-Location (Join-Path $Root 'docs')
    try {
        $r = dotnet run --project $Project -- build 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Docs build failed`n$r" }
        $r | Select-String 'Build complete|Pages:' | ForEach-Object { Write-Host "  $_" }
        Write-Success "Docs site built successfully"
    } finally {
        Pop-Location
    }
}

function Invoke-Serve {
    Write-Step "Starting dev server on port $Port ..."
    Push-Location (Join-Path $Root 'docs')
    try {
        dotnet run --project $Project -- serve --port $Port
    } finally {
        Pop-Location
    }
}

function Invoke-Preview {
    Write-Step "Starting preview server on port $Port ..."
    Push-Location (Join-Path $Root 'docs')
    try {
        dotnet run --project $Project -- preview --port $Port
    } finally {
        Pop-Location
    }
}

# ── main ─────────────────────────────────────────────────────────────
try {
    switch ($Target) {
        'Build'   { Invoke-Build }
        'Publish' { Invoke-Publish }
        'Clean'   { Invoke-Clean }
        'Test'    { Invoke-Test }
        'Serve'   { Invoke-Serve }
        'Preview' { Invoke-Preview }
    }
    Write-Host "`nDone." -ForegroundColor Green
    exit 0
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
