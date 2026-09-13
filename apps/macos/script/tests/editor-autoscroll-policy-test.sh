#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
VIEW="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWebContainerView.swift"

fail() {
    echo "FAIL: $1" >&2
    exit 1
}

# Autoscroll must live in the macOS host so ProseMirror/keyboard selection is
# never intercepted by document-level mouse or selectionchange handlers.
grep -Fq 'override func mouseDragged(with event: NSEvent)' "$VIEW" \
    || fail "WKWebView must observe drag autoscroll at the host layer"
grep -Fq 'override func mouseUp(with event: NSEvent)' "$VIEW" \
    || fail "autoscroll must stop on mouseup"
grep -Fq 'isHandlingSyntheticDrag' "$VIEW" \
    || fail "synthetic drag replay must not recurse into autoscroll"
grep -Fq 'window.scrollBy(0,' "$VIEW" \
    || fail "autoscroll must scroll the editor document"
grep -Fq 'element.scrollTop = next' "$VIEW" \
    || fail "autoscroll must stop when the document boundary is reached"

echo PASS
