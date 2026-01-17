#!/bin/bash
# =============================================================================
# Grigori MCP Server - Multi-Platform Image Builder
# =============================================================================
# Builds Docker images for multiple platforms and optionally pushes to registry.
#
# Usage:
#   ./build-images.sh                    # Build for current platform
#   ./build-images.sh --push             # Build and push to registry
#   ./build-images.sh --platform all     # Build for all platforms
#   ./build-images.sh --slim-only        # Build only slim variant
# =============================================================================

set -e

# Configuration
REGISTRY="${REGISTRY:-}"
IMAGE_NAME="${IMAGE_NAME:-grigori}"
VERSION="${VERSION:-latest}"
PLATFORMS="${PLATFORMS:-linux/amd64}"

# Parse arguments
PUSH=false
SLIM_ONLY=false
FULL_ONLY=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --push)
            PUSH=true
            shift
            ;;
        --platform)
            if [[ "$2" == "all" ]]; then
                PLATFORMS="linux/amd64,linux/arm64"
            else
                PLATFORMS="$2"
            fi
            shift 2
            ;;
        --slim-only)
            SLIM_ONLY=true
            shift
            ;;
        --full-only)
            FULL_ONLY=true
            shift
            ;;
        --registry)
            REGISTRY="$2"
            shift 2
            ;;
        --version)
            VERSION="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Determine full image name
if [[ -n "$REGISTRY" ]]; then
    FULL_IMAGE="${REGISTRY}/${IMAGE_NAME}"
else
    FULL_IMAGE="${IMAGE_NAME}"
fi

echo "=========================================="
echo "Grigori MCP Server - Image Builder"
echo "=========================================="
echo "Image:     ${FULL_IMAGE}"
echo "Version:   ${VERSION}"
echo "Platforms: ${PLATFORMS}"
echo "Push:      ${PUSH}"
echo "=========================================="

# Check for buildx
if ! docker buildx version &> /dev/null; then
    echo "Error: Docker buildx is required for multi-platform builds"
    echo "Install with: docker buildx install"
    exit 1
fi

# Create builder if it doesn't exist
BUILDER_NAME="grigori-builder"
if ! docker buildx inspect ${BUILDER_NAME} &> /dev/null; then
    echo "Creating buildx builder: ${BUILDER_NAME}"
    docker buildx create --name ${BUILDER_NAME} --use --bootstrap
fi

docker buildx use ${BUILDER_NAME}

# Build function
build_image() {
    local target=$1
    local tag_suffix=$2
    local extra_args=""

    if [[ "$PUSH" == "true" ]]; then
        extra_args="--push"
    else
        extra_args="--load"
        # --load only works for single platform
        if [[ "$PLATFORMS" == *","* ]]; then
            echo "Warning: Multi-platform builds require --push. Using --push automatically."
            extra_args="--push"
        fi
    fi

    echo ""
    echo "Building ${FULL_IMAGE}:${tag_suffix}..."
    echo ""

    docker buildx build \
        --platform ${PLATFORMS} \
        --target ${target} \
        --tag "${FULL_IMAGE}:${tag_suffix}" \
        --tag "${FULL_IMAGE}:${VERSION}-${tag_suffix}" \
        ${extra_args} \
        .
}

# Build slim image
if [[ "$FULL_ONLY" != "true" ]]; then
    build_image "slim" "slim"
fi

# Build full image
if [[ "$SLIM_ONLY" != "true" ]]; then
    build_image "full" "full"

    # Tag full as latest
    if [[ "$PUSH" == "true" ]]; then
        echo ""
        echo "Tagging ${FULL_IMAGE}:full as ${FULL_IMAGE}:latest..."
        docker buildx build \
            --platform ${PLATFORMS} \
            --target full \
            --tag "${FULL_IMAGE}:latest" \
            --tag "${FULL_IMAGE}:${VERSION}" \
            --push \
            .
    fi
fi

echo ""
echo "=========================================="
echo "Build complete!"
echo "=========================================="
echo ""
echo "Images built:"
if [[ "$FULL_ONLY" != "true" ]]; then
    echo "  - ${FULL_IMAGE}:slim"
    echo "  - ${FULL_IMAGE}:${VERSION}-slim"
fi
if [[ "$SLIM_ONLY" != "true" ]]; then
    echo "  - ${FULL_IMAGE}:full"
    echo "  - ${FULL_IMAGE}:${VERSION}-full"
    echo "  - ${FULL_IMAGE}:latest"
fi
echo ""
echo "Usage:"
echo "  docker run -it ${FULL_IMAGE}:slim"
echo "  docker run -it ${FULL_IMAGE}:full"
