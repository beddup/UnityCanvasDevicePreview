using System.Collections.Generic;
using UnityEngine;
using System;

namespace CanvasDevicePreview
{
    [ExecuteAlways]
    public class NotchAdapterExample : MonoBehaviour, IPreviewSlotHandler
    {
        [Serializable]
        public class AdjustUIItem
        {
            [Serializable]
            public enum AdjustTypeForTopNotch
            {
                MoveWithFixedHeight, 
                MoveWithFixedBottomPosition
            }
            
            public RectTransform Target;
            public AdjustTypeForTopNotch AdjustType;
            public int MaxOffsetForNotch = 100;
            [NonSerialized] public Vector2 OriginalOffsetMin;
            [NonSerialized] public Vector2 OriginalOffsetMax;
            [NonSerialized] public bool Cached = false;
            
            public void CacheOriginals()
            {
                OriginalOffsetMin = Target.offsetMin;
                OriginalOffsetMax = Target.offsetMax;
                Cached = true;
            }

            public void Restore()
            {
                Target.offsetMin = OriginalOffsetMin;
                Target.offsetMax = OriginalOffsetMax;
            }
        }

        [SerializeField] private List<AdjustUIItem> AdaptingTopNotchTargetsUI;
        [SerializeField] private int m_SimulatedTopNotch;
        [SerializeField] public int m_DesignOffsetForNotch = 60;
        public int TopDeviceNotch => Math.Max(m_SimulatedTopNotch, 0);
        public float TopCanvasNotch
        {
            get
            {
                float scaleFactor = 1f;
                var canvas = GetComponentInParent<Canvas>();
                if (canvas != null) scaleFactor = canvas.scaleFactor;
                float canvasNotch = TopDeviceNotch / scaleFactor - m_DesignOffsetForNotch;
                return Math.Max(canvasNotch, 0);
            }
        }
        
        private bool m_editMode
        {
            get
            {
            #if UNITY_EDITOR
                return !Application.isPlaying;
            #endif
                return false;
            }
        }
        
        public void AdjustTopNotchTargetsUI()
        {
            if (AdaptingTopNotchTargetsUI == null || AdaptingTopNotchTargetsUI.Count == 0) return;
            if (m_editMode)
            {
                foreach (var item in AdaptingTopNotchTargetsUI)
                {
                    if (item.Target == null) continue;
                    if (m_editMode && !item.Cached) item.CacheOriginals();
                    if (m_editMode) item.Restore();
                }
            }

            float canvasNotch = TopCanvasNotch;
            if (canvasNotch <= 0) return;

            foreach (var item in AdaptingTopNotchTargetsUI)
            {
                if (item.Target == null) continue;
                float itemOffset = Math.Min(canvasNotch, item.MaxOffsetForNotch);
                switch (item.AdjustType)
                {
                    case AdjustUIItem.AdjustTypeForTopNotch.MoveWithFixedHeight:
                        var offsetMin = item.Target.offsetMin;
                        var offsetMax = item.Target.offsetMax;
                        offsetMin.y -= itemOffset;
                        offsetMax.y -= itemOffset;
                        item.Target.offsetMin = offsetMin;
                        item.Target.offsetMax = offsetMax;
                        break;

                    case AdjustUIItem.AdjustTypeForTopNotch.MoveWithFixedBottomPosition:
                        var max = item.Target.offsetMax;
                        max.y -= itemOffset;
                        item.Target.offsetMax = max;
                        break;
                }
            }
        }

        public void OnPreviewSlotBuilt(PreviewSlotInfo slotInfo)
        {
            if (slotInfo.DeviceNotchHeight <= 0) return;
            m_SimulatedTopNotch = slotInfo.DeviceNotchHeight;
            AdjustTopNotchTargetsUI();
            Debug.Log($"Apply Top Notch Offset for Device {slotInfo.DeviceLabel}({slotInfo.DeviceResolution}), base offset {TopCanvasNotch}");
        }
        
        #if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_editMode) AdjustTopNotchTargetsUI();
        }
        #endif
    }
}
