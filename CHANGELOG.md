# Changelog
## [0.3.3] - 2026-08-31
### Changed
- Raise Android max texture size to 4096 and use ASTC compression for device overlay textures

### Fixed
- Fix device overlay rendering: screen is now positioned inside the device frame using border size (`StretchToFill`) instead of being scaled to the overlay bounds
- Correct `borderSize` values for iPhone 15, iPhone 15 Pro, iPhone 15 Pro Max, and iPhone 16 Pro presets

## [0.3.2] - 2026-08-28
### Added
- Add `platform` field to the device info broadcast (`"ios"` / `"android"`), parsed from device preset `systemInfo.operatingSystem`

## [0.3.1] - 2026-08-28
### Fixed
- Fix compile error: `HasDeviceNotchSimulationHandler` still referenced the renamed `SimulateDeviceNotch` message instead of `SimulateDevice`

## [0.3.0] - 2026-08-28
### Added
- Broadcast device model, resolution, and notch height to preview clones via `SimulateDevice(Dictionary<string, object>)`
- Parse and expose `deviceModel` from device preset system info

### Changed
- Renamed runtime callback `SimulateDeviceNotch(int)` to `SimulateDevice(Dictionary<string, object>)`

## [0.2.1] - 2026-08-27
### Fixed
- Prevent preview clone from being persisted into the scene when `ShowInHierarchy` is enabled (`HideFlags.DontSave` instead of `HideFlags.None`)

## [0.2.0] - 2026-08-27
### Added
- Prefab source mode: preview a prefab as the source instead of a scene Canvas
- Play mode support with manual refresh under play mode

### Changed
- Refined copy component and notch height messaging

### Fixed
- Fix obsolete API usage

### Removed
- Removed `IPreviewSlotHandler` interface and `PreviewSlotInfo` struct; replaced with `SimulateDeviceNotch(int)` callback

## [0.1.2] - 2026-07-27
### Changed
- Add devices from Assets

## [0.1.1] - 2026-07-27
### Fix
- Fix compile error on 2021.3

## [0.1.0] - 2026-07-03
### Added
- Configurable preview column count with slider control (1-8 columns)

## [0.0.4] - 2026-07-03
### Changed
- Refined window layout: top-bottom split with 4-column upper control area
- Moved layout, image/text, and button tools into separate columns
- Removed Active Devices list (manage via dropdown and preview slot close)
- Simplified toolbar: removed Auto Refresh controls, moved Refresh button to the right

## [0.0.3] - 2026-07-02
- Registered on OpenUPM

## [0.0.2] - 2026-06-28
- Added device overlay rendering with optional device frames
- Added notch height and safe-area computation
- Added preview callbacks (IPreviewSlotHandler) for per-slot layout adaptation
- Added local device preset support under Editor/Devices

## [0.0.1] - 2026-06-23
- Initial release with multi-resolution Canvas preview, selection highlighting, and anchor/image/button quick tools
