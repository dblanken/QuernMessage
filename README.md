# Quern Message

A Vintage Story 1.21.6 mod that displays helpful messages when invalid items are placed in querns.

## Features

- Automatically detects when items are placed in a quern
- Checks if the item can actually be ground
- Displays a friendly message if the item is invalid
- Works with all quern types in the game

## Installation

### Option 1: Using the install script (Linux with Flatpak)

1. Build the mod:
   ```bash
   ./build.sh
   ```

2. Install the mod:
   ```bash
   ./install.sh
   ```

3. Restart Vintage Story

### Option 2: Manual installation

1. Build the mod:
   ```bash
   dotnet build
   ```

2. Find the built mod in `Releases/QuernMessage_v1.0.0.zip`

3. Copy the zip file to your Vintage Story mods directory:
   - **Windows**: `%APPDATA%/VintagestoryData/Mods/`
   - **Linux**: `~/.config/VintagestoryData/Mods/`
   - **Linux (Flatpak)**: `~/.var/app/at.vintagestory.VintageStory/config/VintagestoryData/Mods/`
   - **Mac**: `~/Library/Application Support/VintagestoryData/Mods/`

4. Restart Vintage Story

## Usage

Simply place items in a quern as you normally would. If the item cannot be ground, you'll see a message in chat letting you know that the item is invalid and you need to place something else.

## Development

### Building

```bash
./build.sh
```

### Requirements

- .NET 8.0 SDK
- Vintage Story 1.21.6

## License

Created by Neimoon

## Version History

- **1.0.0** - Initial release
  - Basic validation of quern inputs
  - Chat message notifications for invalid items
