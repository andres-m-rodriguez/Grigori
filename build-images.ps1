# =============================================================================
# Grigori MCP Server - Multi-Platform Image Builder (PowerShell)
# =============================================================================
# Builds Docker images for multiple platforms and optionally pushes to registry.
#
# Usage:
#   .\build-images.ps1                       # Build for current platform
#   .\build-images.ps1 -Push                 # Build and push to registry
#   .\build-images.ps1 -Platform all         # Build for all platforms
#   .\build-images.ps1 -SlimOnly             # Build only slim variant
# =============================================================================

param(
    [switch]$Push,
    [switch]$SlimOnly,
    [switch]$FullOnly,
    [string]$Platform = "linux/amd64",
    [string]$Registry = "",
    [string]$ImageName = "grigori",
    [string]$Version = "latest"
)

$ErrorActionPreference = "Stop"

# Handle 'all' platform
if ($Platform -eq "all") {
    $Platform = "linux/amd64,linux/arm64"
}

# Determine full image name
if ($Registry) {
    $FullImage = "$Registry/$ImageName"
} else {
    $FullImage = $ImageName
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Grigori MCP Server - Image Builder" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Image:     $FullImage"
Write-Host "Version:   $Version"
Write-Host "Platforms: $Platform"
Write-Host "Push:      $Push"
Write-Host "==========================================" -ForegroundColor Cyan

# Check for buildx
try {
    docker buildx version | Out-Null
} catch {
    Write-Error "Docker buildx is required for multi-platform builds"
    exit 1
}

# Create builder if it doesn't exist
$BuilderName = "grigori-builder"
$builderExists = docker buildx inspect $BuilderName 2>$null
if (-not $builderExists) {
    Write-Host "Creating buildx builder: $BuilderName" -ForegroundColor Yellow
    docker buildx create --name $BuilderName --use --bootstrap
}

docker buildx use $BuilderName

function Build-Image {
    param(
        [string]$Target,
        [string]$TagSuffix
    )

    $extraArgs = @()
    if ($Push) {
        $extraArgs += "--push"
    } else {
        if ($Platform -match ",") {
            Write-Host "Warning: Multi-platform builds require --push. Using --push automatically." -ForegroundColor Yellow
            $extraArgs += "--push"
        } else {
            $extraArgs += "--load"
        }
    }

    Write-Host ""
    Write-Host "Building ${FullImage}:${TagSuffix}..." -ForegroundColor Green
    Write-Host ""

    $buildArgs = @(
        "buildx", "build",
        "--platform", $Platform,
        "--target", $Target,
        "--tag", "${FullImage}:${TagSuffix}",
        "--tag", "${FullImage}:${Version}-${TagSuffix}"
    ) + $extraArgs + @(".")

    & docker @buildArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for ${FullImage}:${TagSuffix}"
    }
}

# Build slim image
if (-not $FullOnly) {
    Build-Image -Target "slim" -TagSuffix "slim"
}

# Build full image
if (-not $SlimOnly) {
    Build-Image -Target "full" -TagSuffix "full"

    # Tag full as latest
    if ($Push) {
        Write-Host ""
        Write-Host "Tagging ${FullImage}:full as ${FullImage}:latest..." -ForegroundColor Green

        $buildArgs = @(
            "buildx", "build",
            "--platform", $Platform,
            "--target", "full",
            "--tag", "${FullImage}:latest",
            "--tag", "${FullImage}:${Version}",
            "--push",
            "."
        )

        & docker @buildArgs
    }
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Build complete!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Images built:"
if (-not $FullOnly) {
    Write-Host "  - ${FullImage}:slim"
    Write-Host "  - ${FullImage}:${Version}-slim"
}
if (-not $SlimOnly) {
    Write-Host "  - ${FullImage}:full"
    Write-Host "  - ${FullImage}:${Version}-full"
    Write-Host "  - ${FullImage}:latest"
}
Write-Host ""
Write-Host "Usage:"
Write-Host "  docker run -it ${FullImage}:slim"
Write-Host "  docker run -it ${FullImage}:full"
