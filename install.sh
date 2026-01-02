#!/bin/bash
# Install script for Quern Message mod

MODS_DIR="$HOME/.var/app/at.vintagestory.VintageStory/config/VintagestoryData/Mods"
RELEASES_DIR="Releases"

# Remove any existing QuernMessage mods from the mods directory
echo "Removing old QuernMessage mods..."
rm -f "$MODS_DIR"/QuernMessage_*.zip

# Find the latest release (highest version number)
LATEST_MOD=$(ls -1 "$RELEASES_DIR"/QuernMessage_v*.zip 2>/dev/null | sort -V | tail -1)

if [ -z "$LATEST_MOD" ]; then
    echo "Error: No QuernMessage mod found in $RELEASES_DIR"
    exit 1
fi

# Copy the latest mod to the mods directory
echo "Installing $LATEST_MOD to $MODS_DIR..."
cp "$LATEST_MOD" "$MODS_DIR/"

echo "Installation complete! Restart Vintage Story to load the updated mod."
