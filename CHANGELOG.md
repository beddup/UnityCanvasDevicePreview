# Changelog
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
