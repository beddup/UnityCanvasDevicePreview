using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

namespace CanvasDevicePreview.Editor
{
    public partial class CanvasDevicePreviewWindow
    {
        // ── Prefab 预览源：自动跟随 Project 选中 ──

        private static void DestroyObj(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        private static bool IsPrefabAsset(UnityEngine.Object o)
        {
            var go = o as GameObject;
            return go != null && !go.scene.IsValid()
                && PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab;
        }

        private void SetPrefabSource(GameObject prefab)
        {
            if (_prefabAsset == null)
                _sceneCanvas = _sourceCanvas;   // 记住当前场景 Canvas，退出时恢复

            DestroyPreviewHost();
            _prefabAsset = prefab;
            BuildPrefabHost(prefab);

            _sourceCanvas = _previewHostCanvas;
            ResetSelection();
            RefreshPreviews();
            Repaint();
        }

        private void ClearPrefabSource()
        {
            DestroyPreviewHost();
            _prefabAsset = null;
            _sourceCanvas = _sceneCanvas;
            _renderer.DestroyAll();
        }

        private void BuildPrefabHost(GameObject prefab)
        {
            _previewHost = new GameObject("[CDP] Prefab Preview Host") { hideFlags = HideFlags.HideAndDontSave };
            _previewHost.SetActive(false);

            // 临时正交相机（仅作 worldCamera 设置模板，不参与场景渲染）
            var camGO = new GameObject("[CDP] Host Camera") { hideFlags = HideFlags.HideAndDontSave };
            camGO.transform.SetParent(_previewHost.transform, false);
            _previewHostCamera = camGO.AddComponent<Camera>();
            _previewHostCamera.orthographic = true;
            _previewHostCamera.enabled = false;
            _previewHostCamera.clearFlags = CameraClearFlags.SolidColor;
            _previewHostCamera.backgroundColor = Color.clear;

            var prefabCanvas = prefab.GetComponent<Canvas>();
            if (prefabCanvas != null)
            {
                // prefab 本身就是 Canvas：直接实例化并配置
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _previewHost.transform);
                var canvas = inst.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = _previewHostCamera;
                if (inst.GetComponent<CanvasScaler>() == null)
                    CopySceneCanvasScaler(inst);
                _previewHostCanvas = canvas;
            }
            else
            {
                // 裸 prefab：套一层宿主 Canvas，缩放沿用场景 CanvasScaler 的设置
                var canvasGO = new GameObject($"{prefab.name} (Host Canvas)") { hideFlags = HideFlags.HideAndDontSave };
                canvasGO.transform.SetParent(_previewHost.transform, false);
                var canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = _previewHostCamera;
                CopySceneCanvasScaler(canvasGO);
                _previewHostCanvas = canvas;

                PrefabUtility.InstantiatePrefab(prefab, canvasGO.transform);
            }

            SetHideFlagsRecursive(_previewHost, HideFlags.HideAndDontSave);
            _previewHost.SetActive(true);
        }

        /// <summary>
        /// 给宿主补一个 CanvasScaler，设置复用场景 Canvas 的 CanvasScaler；
        /// 场景没有 CanvasScaler 时回退到 1080×1920 竖屏默认。
        /// </summary>
        private void CopySceneCanvasScaler(GameObject dst)
        {
            var srcCanvas = _sceneCanvas != null ? _sceneCanvas : GameObject.FindAnyObjectByType<Canvas>();
            var src = srcCanvas != null ? srcCanvas.GetComponent<CanvasScaler>() : null;

            var d = dst.AddComponent<CanvasScaler>();
            if (src != null)
            {
                d.uiScaleMode = src.uiScaleMode;
                d.referenceResolution = src.referenceResolution;
                d.screenMatchMode = src.screenMatchMode;
                d.matchWidthOrHeight = src.matchWidthOrHeight;
                d.referencePixelsPerUnit = src.referencePixelsPerUnit;
            }
            else
            {
                Debug.LogWarning("[CanvasDevicePreview] 场景 Canvas 缺少 CanvasScaler，预览将使用默认参考分辨率 1080×1920。建议为场景 Canvas 添加 CanvasScaler。");
                d.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                d.referenceResolution = new Vector2(1080, 1920);
                d.matchWidthOrHeight = 0.5f;
            }
        }

        private void DestroyPreviewHost()
        {
            if (_previewHost != null)
            {
                DestroyObj(_previewHost);
                _previewHost = null;
            }
            _previewHostCamera = null;
            _previewHostCanvas = null;
        }

        private void ResetSelection()
        {
            _selectedGameObjects = new List<GameObject>();
            _selectedRectTransforms = new List<RectTransform>();
            _selectedGo = null;
            _selectedRt = null;
            _selectedImage = null;
            _selectedButton = null;
            _selectedTextGameObject = null;
            _horizontalEdge = HorizontalEdge.None;
            _verticalEdge = VerticalEdge.None;
        }

        private static void SetHideFlagsRecursive(GameObject root, HideFlags flags)
        {
            root.hideFlags = flags;
            foreach (Transform t in root.transform)
                SetHideFlagsRecursive(t.gameObject, flags);
        }
    }
}
