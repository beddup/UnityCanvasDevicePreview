using UnityEngine;

namespace CanvasDevicePreview
{
    public struct PreviewSlotInfo
    {
        public string DeviceLabel;
        public Vector2Int DeviceResolution;
        public int DeviceNotchHeight;
        public Canvas PreviewCanvas;
    }
    public interface IPreviewSlotHandler
    {
        void OnPreviewSlotBuilt(PreviewSlotInfo slotInfo);
    }
}
