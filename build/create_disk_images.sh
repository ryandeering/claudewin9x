#!/bin/bash
# Create FAT12 floppy and ISO9660 disk images for the Win9x client.
# Requires: mtools, genisoimage (apt install mtools genisoimage)

set -e

EXE="${1:-ClaudeWin9x-Client.exe}"
INI="${2:-client.ini}"

IMG="claudewin9x.img"
ISO="claudewin9x.iso"
VOLLABEL="CLAUDEWIN9X"

if [[ ! -f "$EXE" || ! -f "$INI" ]]; then
    echo "Error: $EXE or $INI not found" >&2
    exit 1
fi

echo "EXE: $(stat -c%s "$EXE") bytes, INI: $(stat -c%s "$INI") bytes"

# Create 1.44MB FAT12 floppy image
dd if=/dev/zero of="$IMG" bs=1474560 count=1 2>/dev/null
mformat -i "$IMG" -f 1440 -v "$VOLLABEL" ::
mcopy -i "$IMG" "$EXE" "::CLAUDWIN9X.EXE"
mcopy -i "$IMG" "$INI" "::CLIENT.INI"
echo "Created $IMG ($(stat -c%s "$IMG") bytes)"

# Create ISO9660 CD-ROM image
TMPDIR=$(mktemp -d)
cp "$EXE" "$TMPDIR/$(basename "$EXE")"
cp "$INI" "$TMPDIR/CLIENT.INI"
genisoimage -o "$ISO" -V "$VOLLABEL" -r -J "$TMPDIR" 2>/dev/null
rm -rf "$TMPDIR"
echo "Created $ISO ($(stat -c%s "$ISO") bytes)"
