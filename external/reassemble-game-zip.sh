#!/bin/sh
# Reassemble the embedded-game archive (split for GitHub 100MB file limit)
cd "$(dirname "$0")"
cat Game.zip.part-* > Game.zip
echo "external/Game.zip reassembled: $(wc -c < Game.zip) bytes"
