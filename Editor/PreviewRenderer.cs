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
    public partial class PreviewRenderer
    {
        public IReadOnlyList<PreviewSlot> Slots => _slots;
        private readonly List<PreviewSlot> _slots = new();

        // 勾选后预览克隆对象在 Hierarchy 可见（hideFlags=None），否则隐藏（HideAndDontSave）
        public bool ShowInHierarchy { get; set; }
        private HideFlags hideFlags => ShowInHierarchy ? HideFlags.None : HideFlags.HideAndDontSave;

        private void DestroyObj(UnityEngine.Object obj)
        {
            if (obj == null) return;
            // 预览克隆/相机/RT 都是编辑器临时对象（HideAndDontSave），与 gameplay 无关。
            // 一律立即销毁：Play Mode 下 Object.Destroy 会延迟到帧末，刷新频繁时旧克隆残留可见。
            UnityEngine.Object.DestroyImmediate(obj);
        }

        public void Rebuild(Canvas sourceCanvas,
                            Camera sourceCamera,
                            List<string> activeKeys,
                            Dictionary<string, Vector2Int> resolutionLookup,
                            DeviceDatabase deviceDb,
                            Dictionary<string, int> customNotchHeights = null,
                            List<RectTransform> selectedRTs = null)
        {
            if (sourceCanvas == null || !sourceCanvas) return;
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
                    DestroyObj(slot.CloneRoot);

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
            
            for (int i = 0; i < _slots.Count; i ++)
            {
                float offset = sourceCamera.orthographicSize * 2 * sourceCamera.aspect * 2;
                _slots[i].Camera.transform.position = sourceCamera.transform.position + (i + 1) * offset * Vector3.right;
            }
        }

        private float GetContentScale(GameObject cloneRoot, Vector2Int res)
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

        private int GetDeviceTopNotch(string key, DeviceDatabase deviceDb, Dictionary<string, int> customNotchHeights)
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
        private void AddHighlights(Canvas sourceCanvas, PreviewSlot slot, List<RectTransform> selectedRTs)
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
                    hideFlags = hideFlags
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
        private RectTransform FindCorrespondingRectTransform(
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

            var camGO = new GameObject($"[CDP] Cam {key}") { hideFlags = hideFlags };
            var cam = camGO.AddComponent<Camera>();
            ConfigurePreviewCamera(cam, sourceCamera, res);

            var rt = new RenderTexture(res.x, res.y, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = hideFlags,
                name = $"[CDP] RT {key}"
            };
            rt.Create();
            cam.targetTexture = rt;

            GameObject clone = null;
            PreviewSlot slot = null;
            try
            {
                clone = BuildClone(sourceCanvas, key, res, cam);
                if (clone == null)
                {
                    DestroyObj(camGO);
                    rt.Release();
                    DestroyObj(rt);
                    return null;
                }

                int notchHeight = GetDeviceTopNotch(key, deviceDb, customNotchHeights);

                slot = new PreviewSlot
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
            catch
            {
                // 创建中途失败时清理已创建的临时对象，避免 clone/cam/RT 残留
                if (slot != null) DestroySlot(slot);
                else
                {
                    if (clone != null) DestroyObj(clone);
                    if (camGO != null) DestroyObj(camGO);
                    if (rt != null) { rt.Release(); DestroyObj(rt); }
                }
                throw;
            }
        }

        private void ConfigurePreviewCamera(Camera previewCamera, Camera sourceCamera, Vector2Int res)
        {
            previewCamera.CopyFrom(sourceCamera);
            previewCamera.enabled = false;
            previewCamera.targetTexture = null;
            previewCamera.aspect = (float)res.x / res.y;
        }

        private float ComputePreviewCanvasNotch(PreviewSlot slot)
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
        private void BroadcastSlotInfo(PreviewSlot slot)
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

        private GameObject BuildClone(Canvas sourceCanvas, string key, Vector2Int res, Camera cam)
        {
            var cloneGO = UnityEngine.Object.Instantiate(sourceCanvas.gameObject);
            cloneGO.name = $"[CDP] {sourceCanvas.name} {key}";
            cloneGO.hideFlags = hideFlags;
            cloneGO.SetActive(true);

            var cloneCanvas = cloneGO.GetComponent<Canvas>();
            if (cloneCanvas == null)
            {
                DestroyObj(cloneGO);
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

        private void DestroySlot(PreviewSlot slot)
        {
            if (slot.RenderTexture != null)
            {
                slot.RenderTexture.Release();
                DestroyObj(slot.RenderTexture);
            }
            if (slot.Camera != null)
                DestroyObj(slot.Camera.gameObject);
            if (slot.CloneRoot != null)
                DestroyObj(slot.CloneRoot);
            if (slot.OverlayTexture != null)
                DestroyObj(slot.OverlayTexture);
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
