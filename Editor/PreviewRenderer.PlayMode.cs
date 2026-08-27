using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditorInternal;

namespace CanvasDevicePreview.Editor
{
    public partial class PreviewRenderer
    {
        // ── Play Mode: visual-only clone (no runtime scripts) ──

        private static void DestroyObj(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        /// <summary>
        /// 在 Play Mode 下构建"仅视觉"克隆：复制 RectTransform 与 uGUI 视觉/布局组件，
        /// 不复制任何自定义脚本，从而避免克隆执行重复的游戏逻辑。
        /// </summary>
        private static GameObject BuildVisualClone(Canvas sourceCanvas, string key, Vector2Int res, Camera cam)
        {
            var cloneGO = new GameObject($"[CDP] {sourceCanvas.name} {key}")
            {
                // hideFlags = HideFlags.HideAndDontSave,
                layer = sourceCanvas.gameObject.layer,
            };

            var cloneRt = cloneGO.AddComponent<RectTransform>();
            CopyRectTransform(sourceCanvas.transform, cloneRt, res);

            var cloneCanvas = cloneGO.AddComponent<Canvas>();
            cloneCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            cloneCanvas.worldCamera = cam;
            cloneCanvas.planeDistance = 1f;
            cloneCanvas.overrideSorting = sourceCanvas.overrideSorting;
            cloneCanvas.sortingLayerID = sourceCanvas.sortingLayerID;
            cloneCanvas.sortingOrder = sourceCanvas.sortingOrder;

            CopyCanvasScaler(sourceCanvas, cloneGO);
            RecurseVisualCopy(sourceCanvas.transform, cloneGO.transform);

            cloneGO.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(cloneRt);
            Canvas.ForceUpdateCanvases();
            return cloneGO;
        }

        private static void CopyCanvasScaler(Canvas sourceCanvas, GameObject dst)
        {
            var src = sourceCanvas.GetComponent<CanvasScaler>();
            if (src == null) return;
            var d = dst.AddComponent<CanvasScaler>();
            ComponentUtility.CopyComponent(src);
            ComponentUtility.PasteComponentValues(d);
        }

        private static void RecurseVisualCopy(Transform src, Transform dst)
        {
            for (int i = 0; i < src.childCount; i++)
            {
                Transform child = src.GetChild(i);
                var childGO = new GameObject(child.name)
                {
                    // hideFlags = HideFlags.HideAndDontSave,
                    layer = child.gameObject.layer,
                };
                childGO.transform.SetParent(dst, false);
                childGO.SetActive(child.gameObject.activeSelf);

                var childRt = child as RectTransform;
                if (childRt != null)
                {
                    var dstRt = childGO.AddComponent<RectTransform>();
                    CopyRectTransform(childRt, dstRt, null);
                    CopyVisualComponents(child.gameObject, childGO);
                }
                else
                {
                    childGO.transform.localPosition = child.localPosition;
                    childGO.transform.localRotation = child.localRotation;
                    childGO.transform.localScale = child.localScale;
                }

                RecurseVisualCopy(child, childGO.transform);
            }
        }

        private static void CopyRectTransform(Transform src, RectTransform dst, Vector2Int? sizeOverride)
        {
            dst.localPosition = src.localPosition;
            dst.localRotation = src.localRotation;
            dst.localScale = src.localScale;

            var srcRt = src as RectTransform;
            if (srcRt == null) return;

            dst.anchorMin = srcRt.anchorMin;
            dst.anchorMax = srcRt.anchorMax;
            dst.pivot = srcRt.pivot;
            dst.sizeDelta = sizeOverride.HasValue
                ? new Vector2(sizeOverride.Value.x, sizeOverride.Value.y)
                : srcRt.sizeDelta;
            dst.anchoredPosition = srcRt.anchoredPosition;
        }

        private static void CopyVisualComponents(GameObject src, GameObject dst)
        {
            // 剪贴板是全局单例：Copy 后必须立即 Paste
            foreach (var c in src.GetComponents<Component>())
            {
                if (!ShouldCopyComponent(c)) continue;
                ComponentUtility.CopyComponent(c);
                ComponentUtility.PasteComponentAsNew(dst);
            }

            // 保持预览不拦截射线（源值可能是 true）
            foreach (var g in dst.GetComponentsInChildren<Graphic>())
                g.raycastTarget = false;
        }

        private static bool ShouldCopyComponent(Component c)
        {
            string ns = c.GetType().Namespace ?? "";
            bool isUiOrTmp = ns == "UnityEngine.UI" || ns.StartsWith("UnityEngine.UI.")
                          || ns == "TMPro"        || ns.StartsWith("TMPro.");
            // Graphic 子类（含自定义与 Spine 的 SkeletonGraphic，任意命名空间）都是视觉组件，需复制；
            // CanvasGroup 在 UnityEngine 命名空间、非 Graphic，需显式加入。
            if (!isUiOrTmp && !(c is Graphic) && !(c is CanvasGroup)) return false;

            if (c is CanvasScaler) return false;      // root 已手工复制
            if (c is GraphicRaycaster) return false;  // 预览无需射线
            if (c is Selectable) return false;        // 避免复制 onClick 等 UnityEvent 引用
            return true;
        }
    }
}
