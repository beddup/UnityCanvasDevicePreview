using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
                hideFlags = HideFlags.HideAndDontSave,
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
            d.uiScaleMode = src.uiScaleMode;
            d.referenceResolution = src.referenceResolution;
            d.screenMatchMode = src.screenMatchMode;
            d.matchWidthOrHeight = src.matchWidthOrHeight;
            d.referencePixelsPerUnit = src.referencePixelsPerUnit;
        }

        private static void RecurseVisualCopy(Transform src, Transform dst)
        {
            for (int i = 0; i < src.childCount; i++)
            {
                Transform child = src.GetChild(i);
                var childGO = new GameObject(child.name)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = child.gameObject.layer,
                };
                childGO.transform.SetParent(dst, false);

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
            // 图形组件
            var img = src.GetComponent<Image>();
            if (img != null)
            {
                var d = dst.AddComponent<Image>();
                d.sprite = img.sprite;
                d.color = img.color;
                d.type = img.type;
                d.fillCenter = img.fillCenter;
                d.fillAmount = img.fillAmount;
                d.fillMethod = img.fillMethod;
                d.fillOrigin = img.fillOrigin;
                d.fillClockwise = img.fillClockwise;
                d.preserveAspect = img.preserveAspect;
                d.raycastTarget = false;
            }
            else
            {
                var raw = src.GetComponent<RawImage>();
                if (raw != null)
                {
                    var d = dst.AddComponent<RawImage>();
                    d.texture = raw.texture;
                    d.color = raw.color;
                    d.uvRect = raw.uvRect;
                    d.material = raw.material;
                    d.raycastTarget = false;
                }
            }

            // 文本组件
            var text = src.GetComponent<Text>();
            if (text != null)
            {
                var d = dst.AddComponent<Text>();
                d.text = text.text;
                d.font = text.font;
                d.fontSize = text.fontSize;
                d.color = text.color;
                d.alignment = text.alignment;
                d.fontStyle = text.fontStyle;
                d.lineSpacing = text.lineSpacing;
                d.supportRichText = text.supportRichText;
                d.horizontalOverflow = text.horizontalOverflow;
                d.verticalOverflow = text.verticalOverflow;
                d.raycastTarget = false;
            }

            var tmp = src.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                var d = (TMP_Text)dst.AddComponent(tmp.GetType());
                d.text = tmp.text;
                d.font = tmp.font;
                d.fontSize = tmp.fontSize;
                d.color = tmp.color;
                d.alignment = tmp.alignment;
                d.fontStyle = tmp.fontStyle;
                d.textWrappingMode = tmp.textWrappingMode;
                d.raycastTarget = false;
            }

            // 布局组件
            var le = src.GetComponent<LayoutElement>();
            if (le != null)
            {
                var d = dst.AddComponent<LayoutElement>();
                d.minWidth = le.minWidth;
                d.preferredWidth = le.preferredWidth;
                d.flexibleWidth = le.flexibleWidth;
                d.minHeight = le.minHeight;
                d.preferredHeight = le.preferredHeight;
                d.flexibleHeight = le.flexibleHeight;
                d.layoutPriority = le.layoutPriority;
            }

            var csf = src.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                var d = dst.AddComponent<ContentSizeFitter>();
                d.horizontalFit = csf.horizontalFit;
                d.verticalFit = csf.verticalFit;
            }

            var arf = src.GetComponent<AspectRatioFitter>();
            if (arf != null)
            {
                var d = dst.AddComponent<AspectRatioFitter>();
                d.aspectMode = arf.aspectMode;
                d.aspectRatio = arf.aspectRatio;
            }

            var hlg = src.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) { var d = dst.AddComponent<HorizontalLayoutGroup>(); CopyLayoutGroupFields(hlg, d); }
            var vlg = src.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) { var d = dst.AddComponent<VerticalLayoutGroup>(); CopyLayoutGroupFields(vlg, d); }
            var glg = src.GetComponent<GridLayoutGroup>();
            if (glg != null)
            {
                var d = dst.AddComponent<GridLayoutGroup>();
                d.padding = glg.padding;
                d.childAlignment = glg.childAlignment;
                d.cellSize = glg.cellSize;
                d.spacing = glg.spacing;
                d.startCorner = glg.startCorner;
                d.startAxis = glg.startAxis;
                d.constraint = glg.constraint;
                d.constraintCount = glg.constraintCount;
            }

            // 遮罩
            var mask = src.GetComponent<Mask>();
            if (mask != null) { var d = dst.AddComponent<Mask>(); d.showMaskGraphic = mask.showMaskGraphic; }
            var rmask = src.GetComponent<RectMask2D>();
            if (rmask != null) { var d = dst.AddComponent<RectMask2D>(); d.padding = rmask.padding; }

            // CanvasGroup
            var cg = src.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                var d = dst.AddComponent<CanvasGroup>();
                d.alpha = cg.alpha;
                d.interactable = cg.interactable;
                d.blocksRaycasts = cg.blocksRaycasts;
                d.ignoreParentGroups = cg.ignoreParentGroups;
            }
        }

        private static void CopyLayoutGroupFields(HorizontalOrVerticalLayoutGroup src, HorizontalOrVerticalLayoutGroup dst)
        {
            dst.padding = src.padding;
            dst.spacing = src.spacing;
            dst.childAlignment = src.childAlignment;
            dst.childControlWidth = src.childControlWidth;
            dst.childControlHeight = src.childControlHeight;
            dst.childForceExpandWidth = src.childForceExpandWidth;
            dst.childForceExpandHeight = src.childForceExpandHeight;
            dst.childScaleWidth = src.childScaleWidth;
            dst.childScaleHeight = src.childScaleHeight;
            dst.reverseArrangement = src.reverseArrangement;
        }
    }
}
