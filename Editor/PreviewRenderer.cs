using System;
using System.Collections.Generic;
using CanvasDevicePreview;
using UnityEngine;
using UnityEngine.UI;

namespace CanvasDevicePreview.Editor
{
    /// <summary>
    /// Creates and manages preview slots (RT + Camera + Canvas clone + optional device overlay)
    /// for a given source Canvas at specified resolutions.
    /// </summary>
    public class PreviewRenderer
    {
        public IReadOnlyList<PreviewSlot> Slots => _slots;
        private readonly List<PreviewSlot> _slots = new();

        public void Rebuild(Canvas sourceCanvas,
                            List<string> activeKeys,
                            Dictionary<string, Vector2Int> resolutionLookup,
                            DeviceDatabase deviceDb,
                            Dictionary<string, int> customNotchHeights = null,
                            List<RectTransform> selectedRTs = null)
        {
            if (sourceCanvas == null || !sourceCanvas) return;
            var sourceCamera = sourceCanvas.worldCamera;
            if (sourceCamera == null)
            {
                Debug.LogError($"[PreviewRenderer] Source Canvas '{sourceCanvas.name}' has no worldCamera. Cannot create preview slots.");
                DestroyAll();
                return;
            }

            // 1. remove slots for deselected keys
            for (int i = _slots.Count - 1; i >= 0; i--)
            {
                if (!activeKeys.Contains(_slots[i].Key))
                {
                    DestroySlot(_slots[i]);
                    _slots.RemoveAt(i);
                }
            }

            // 2. rebuild only the clone for existing slots (reuse RT + Camera)
            foreach (var slot in _slots)
            {
                if (slot.CloneRoot != null)
                    UnityEngine.Object.DestroyImmediate(slot.CloneRoot);

                ConfigurePreviewCamera(slot.Camera, sourceCamera, slot.Resolution);
                slot.Camera.targetTexture = slot.RenderTexture;
                slot.CloneRoot = BuildClone(sourceCanvas, slot.Key, slot.Resolution, slot.Camera);
                slot.DeviceNotchHeight = GetDeviceTopNotch(slot.Key, deviceDb, customNotchHeights);
                slot.CanvasNotchHeight = ComputePreviewCanvasNotch(slot);
                BroadcastSlotInfo(slot);
                AddHighlights(sourceCanvas, slot, selectedRTs);
                slot.Camera.Render();
            }

            // 3. create new slots for newly selected keys
            for(int index = 0; index < activeKeys.Count; index ++)
            {
                var key = activeKeys[index];
                if (_slots.Exists(p => p.Key == key)) continue;
                if (!resolutionLookup.TryGetValue(key, out var res)) continue;

                try
                {
                    var slot = BuildFullSlot(sourceCanvas, sourceCamera, key, res, deviceDb, customNotchHeights);
                    if (slot != null)
                    {
                        slot.Camera.transform.position += sourceCamera.transform.position + (index + 1) * 15 * Vector3.right ;
                        _slots.Add(slot);
                        AddHighlights(sourceCanvas, slot, selectedRTs);
                        slot.Camera.Render();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PreviewRenderer] Failed for {key}: {e.Message}");
                }
            }
        }

        private static float GetContentScale(GameObject cloneRoot, Vector2Int res)
        {
            var scaler = cloneRoot?.GetComponent<CanvasScaler>();
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                return 1f;

            Vector2 refRes = scaler.referenceResolution;
            if (refRes.x <= 0 || refRes.y <= 0) return 1f;

            float wScale = res.x / refRes.x;
            float hScale = res.y / refRes.y;

            switch (scaler.screenMatchMode)
            {
                case CanvasScaler.ScreenMatchMode.Expand:
                    return Mathf.Min(wScale, hScale);
                case CanvasScaler.ScreenMatchMode.Shrink:
                    return Mathf.Max(wScale, hScale);
                default: // MatchWidthOrHeight
                    return Mathf.Lerp(wScale, hScale, scaler.matchWidthOrHeight);
            }
        }

        private static int GetDeviceTopNotch(string key, DeviceDatabase deviceDb, Dictionary<string, int> customNotchHeights)
        {
            if (deviceDb != null && deviceDb.TryGetDevice(key, out var device))
                return device.NotchHeight;
            if (customNotchHeights != null && customNotchHeights.TryGetValue(key, out var h))
                return h;
            return 0;
        }

        /// <summary>
        /// Create semi-transparent pink rectangles on a single preview slotʼs clone
        /// to show the position of the currently selected RectTransforms.
        /// </summary>
        private static void AddHighlights(Canvas sourceCanvas, PreviewSlot slot, List<RectTransform> selectedRTs)
        {
            if (selectedRTs == null || selectedRTs.Count == 0) return;
            if (slot.CloneRoot == null) return;

            var canvasRt = sourceCanvas.GetComponent<RectTransform>();
            var cloneCanvasRt = slot.CloneRoot.GetComponent<RectTransform>();
            if (canvasRt == null || cloneCanvasRt == null) return;

            foreach (var selectedRt in selectedRTs)
            {
                if (selectedRt == null || selectedRt == canvasRt) continue;

                var cloneRt = FindCorrespondingRectTransform(canvasRt, cloneCanvasRt, selectedRt);
                if (cloneRt == null) continue;

                var highlightGO = new GameObject("[CDP] Highlight")
                {
                    hideFlags = HideFlags.DontSave
                };
                highlightGO.transform.SetParent(cloneRt, false);
                highlightGO.transform.SetAsLastSibling();

                var rt = highlightGO.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                var img = highlightGO.AddComponent<Image>();
                img.color = new Color(1f, 0.4f, 0.7f, 0.35f);
                img.raycastTarget = false;
            }
        }

        /// <summary>
        /// Find the RectTransform in the clone hierarchy that corresponds to the given
        /// RectTransform in the source hierarchy, using sibling-index paths.
        /// </summary>
        private static RectTransform FindCorrespondingRectTransform(
            RectTransform sourceRoot, RectTransform cloneRoot, RectTransform target)
        {
            // Build path of sibling indices from sourceRoot to target
            var path = new List<int>();
            Transform current = target;
            while (current != null && current != sourceRoot)
            {
                path.Insert(0, current.GetSiblingIndex());
                current = current.parent;
            }

            if (current != sourceRoot) return null;

            // Follow the same path in the clone
            Transform cloneCurrent = cloneRoot;
            foreach (int index in path)
            {
                if (index >= cloneCurrent.childCount) return null;
                cloneCurrent = cloneCurrent.GetChild(index);
            }

            return cloneCurrent as RectTransform;
        }

        public void DestroyAll()
        {
            foreach (var p in _slots)
                DestroySlot(p);
            _slots.Clear();
        }

        private PreviewSlot BuildFullSlot(Canvas sourceCanvas, Camera sourceCamera, string key, Vector2Int res, DeviceDatabase deviceDb, Dictionary<string, int> customNotchHeights = null)
        {
            if (sourceCamera == null)
            {
                Debug.LogError($"[PreviewRenderer] Source Canvas '{sourceCanvas.name}' has no worldCamera. Cannot create preview slot '{key}'.");
                return null;
            }

            var camGO = new GameObject($"[CDP] Cam {key}") { hideFlags = HideFlags.DontSave };
            var cam = camGO.AddComponent<Camera>();
            ConfigurePreviewCamera(cam, sourceCamera, res);

            var rt = new RenderTexture(res.x, res.y, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.DontSave,
                name = $"[CDP] RT {key}"
            };
            rt.Create();
            cam.targetTexture = rt;

            var clone = BuildClone(sourceCanvas, key, res, cam);
            if (clone == null)
            {
                UnityEngine.Object.DestroyImmediate(camGO);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                return null;
            }

            int notchHeight = GetDeviceTopNotch(key, deviceDb, customNotchHeights);

            var slot = new PreviewSlot
            {
                Key = key,
                Label = key,
                Resolution = res,
                DeviceNotchHeight = notchHeight,
                Camera = cam,
                RenderTexture = rt,
                CloneRoot = clone,
            };

            // Load device overlay if available
            if (deviceDb != null && deviceDb.TryGetDevice(key, out var device) && device.OverlayPath != null)
            {
                var overlayTex = deviceDb.LoadOverlayTexture(device.OverlayPath, device.OverlayBasePath);
                if (overlayTex != null)
                {
                    slot.OverlayTexture = overlayTex;
                    slot.BorderSize = device.BorderSize;
                }
            }

            slot.CanvasNotchHeight = ComputePreviewCanvasNotch(slot);
            BroadcastSlotInfo(slot);

            cam.Render();
            return slot;
        }

        private static void ConfigurePreviewCamera(Camera previewCamera, Camera sourceCamera, Vector2Int res)
        {
            previewCamera.CopyFrom(sourceCamera);
            previewCamera.enabled = false;
            previewCamera.targetTexture = null;
            previewCamera.aspect = (float)res.x / res.y;
        }

        private static float ComputePreviewCanvasNotch(PreviewSlot slot)
        {
            if (slot.DeviceNotchHeight <= 0) return 0f;
            var canvas = slot.CloneRoot.GetComponent<Canvas>();
            if (canvas == null) return 0f;
            return slot.DeviceNotchHeight / canvas.scaleFactor;
        }

        /// <summary>
        /// 构建 PreviewSlotInfo 并广播给 clone 上所有实现 IPreviewSlotHandler 的组件，
        /// 由各业务脚本根据设备和 Canvas 信息自行调整布局。
        /// </summary>
        private static void BroadcastSlotInfo(PreviewSlot slot)
        {
            var cloneRoot = slot.CloneRoot;
            if (cloneRoot == null) return;

            var handlers = cloneRoot.GetComponentsInChildren<IPreviewSlotHandler>();
            if (handlers.Length == 0) return;

            var previewCanvas = cloneRoot.GetComponent<Canvas>();
            var info = new PreviewSlotInfo
            {
                DeviceLabel = slot.Label,
                DeviceResolution = slot.Resolution,
                DeviceNotchHeight = slot.DeviceNotchHeight,
                PreviewCanvas = previewCanvas,
            };

            foreach (var handler in handlers)
            {
                handler.OnPreviewSlotBuilt(info);
            }
        }

        private static GameObject BuildClone(Canvas sourceCanvas, string key, Vector2Int res, Camera cam)
        {
            var cloneGO = UnityEngine.Object.Instantiate(sourceCanvas.gameObject);
            cloneGO.name = $"[CDP] {sourceCanvas.name} {key}";
            cloneGO.hideFlags = HideFlags.DontSave;
            cloneGO.SetActive(true);

            var cloneCanvas = cloneGO.GetComponent<Canvas>();
            if (cloneCanvas == null)
            {
                UnityEngine.Object.DestroyImmediate(cloneGO);
                return null;
            }

            cloneCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            cloneCanvas.worldCamera = cam;
            cloneCanvas.planeDistance = 1f;

            var cloneRect = cloneCanvas.GetComponent<RectTransform>();
            if (cloneRect != null)
            {
                cloneRect.sizeDelta = new Vector2(res.x, res.y);
                LayoutRebuilder.ForceRebuildLayoutImmediate(cloneRect);
            }

            Canvas.ForceUpdateCanvases();
            return cloneGO;
        }

        private static void DestroySlot(PreviewSlot slot)
        {
            if (slot.RenderTexture != null)
            {
                slot.RenderTexture.Release();
                UnityEngine.Object.DestroyImmediate(slot.RenderTexture);
            }
            if (slot.Camera != null)
                UnityEngine.Object.DestroyImmediate(slot.Camera.gameObject);
            if (slot.CloneRoot != null)
                UnityEngine.Object.DestroyImmediate(slot.CloneRoot);
            if (slot.OverlayTexture != null)
                UnityEngine.Object.DestroyImmediate(slot.OverlayTexture);
        }
    }

    public class PreviewSlot
    {
        public string Key;
        public string Label;
        public Vector2Int Resolution;
        public int DeviceNotchHeight;
        public float CanvasNotchHeight;
        public Camera Camera;
        public RenderTexture RenderTexture;
        public GameObject CloneRoot;
        public Texture2D OverlayTexture;
        public Vector4 BorderSize;
    }
}
