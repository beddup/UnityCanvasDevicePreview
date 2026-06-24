# Canvas Device Preview

Preview a Unity UI Canvas across multiple device resolutions in one editor window, with device overlays, notch information, selection highlighting, and quick tools for common uGUI cleanup work.

---

## What problem does this solve?

Unity UI often looks correct at the design resolution but breaks on real devices: anchors drift, full-screen artwork crops poorly, sliced sprites are oversized, button hit areas are too small, and safe-area / notch behavior is hard to check without switching Game view resolutions repeatedly.

**Canvas Device Preview** gives UI developers a compact multi-device preview workspace:

| Feature | What it does |
|---|---|
| Multi-resolution preview | Renders one source `Canvas` into multiple selected device resolutions |
| Device presets | Loads `.device` definitions from Unity Device Simulator packages and local package presets |
| Device overlays | Draws optional device frame overlays around preview render textures |
| Notch information | Computes top notch / safe-area height from device safe area data |
| Preview callbacks | Broadcasts `PreviewSlotInfo` to cloned preview canvases through `IPreviewSlotHandler` |
| Selection highlight | Highlights selected `RectTransform` objects inside every preview slot |
| Anchor tools | Quickly set selected UI elements to left / center / right / stretch and top / center / bottom / stretch |
| Image tools | Inspect sprite sizing, set sliced mode, open Sprite Editor, shrink sliced sprites, or add an aspect-ratio filler |
| Button tools | Add a transparent `ClickArea` child for larger hit targets |

The preview is non-destructive: the editor window clones the source Canvas for each target resolution, renders those clones with preview cameras, and destroys them when the window closes.

---

## Who needs this?

- **Unity UI developers** who need to validate uGUI layouts across many phone resolutions.
- **Technical artists** who tune anchors, sliced sprites, and full-screen backgrounds.
- **Teams building safe-area-aware mobile UI** who want to preview notch-specific behavior in the editor.

---

## Dependencies

| Package | Required | Description |
|---|---:|---|
| `com.unity.textmeshpro` | Yes | Used by the editor-side selected text inspection tools |
| `com.unity.ugui` | Usually already installed | Required for Unity UI components such as `Canvas`, `Image`, `Button`, and `CanvasScaler` |
| `com.unity.device-simulator.devices` | Optional | Provides additional `.device` definitions; local package device files are also supported |

---

## Installation

Add this line to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.beddup.unitycanvasdevicepreview": "https://github.com/beddup/unitycanvasdevicepreview.git"
  }
}
```

You can also download the source code directly and import it into your Unity project.

---

## How to use

### Step 1: Open the preview window

In Unity, open:

```text
Window -> Canvas Device Preview
```

The window automatically finds a Canvas in the active scene, or you can assign a source Canvas manually.

### Step 2: Choose target devices

Use **Select Devices...** to enable one or more presets. You can also add a custom resolution by entering:

- `w` - width in pixels
- `h` - height in pixels
- `notch` - optional top notch height in pixels

Each selected device appears as a preview tile on the right side of the window.

### Step 3: Refresh and inspect

Enable **Auto Refresh** to rebuild previews after scene changes, or click **Refresh** manually.

When you select UI objects in the scene hierarchy, their corresponding `RectTransform` areas are highlighted inside each preview slot.

### Step 4: Adjust common UI settings

With one or more `RectTransform` objects selected, use the left-side tools to adjust anchors:

- Horizontal: left, center, right, stretch
- Vertical: top, center, bottom, stretch

When a single object is selected, extra tools may appear:

- `Image`: sliced sprite checks, Sprite Editor shortcut, shrink sliced sprite, aspect-ratio filler
- `Button`: create a transparent `ClickArea` child
- `TMP_Text`: reminders for runtime text range and alignment checks

---

## Preview callbacks

Runtime scripts can implement `IPreviewSlotHandler` to react when a Canvas clone is built for a specific preview slot.

This is useful for safe-area or notch adapters that need to simulate per-device layout behavior inside editor previews.

For a complete notch-adaptation example, see:

```text
Assets/CanvasDevicePreview/Runtime/NotchAdapterExample.cs
```

`NotchAdapterExample` implements `IPreviewSlotHandler`, reads `PreviewSlotInfo.DeviceNotchHeight`,
converts the device notch height into Canvas units, and adjusts configured `RectTransform` targets.
It can be used as a reference for UI that needs to move or resize around the top safe area in each
preview device.

---

## Device presets

Device definitions are loaded from:

1. Unity's `com.unity.device-simulator.devices` package, if installed
2. Local package files under `Editor/Devices`

Local files with the same friendly device name override package-provided definitions. A `.device` file can include an overlay path and border size so the preview window can draw a device frame around the rendered Canvas.

---

## Known limitations

- The source Canvas must have a `worldCamera`; previews are built by copying that Camera's settings.
- The preview focuses on portrait-oriented UI workflows.
- Device overlays are visual only; input simulation is not provided.
- `Undo Shrink Sliced Sprite` restores from `Library/ShrinkSlicedSprites`, so clearing `Library` removes those backups.

---

## License

MIT License. Copyright (c) 2026 Liu Wei.
