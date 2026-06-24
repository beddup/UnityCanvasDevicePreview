using UnityEngine;
using UnityEngine.UI;

namespace CanvasDevicePreview
{
    [ExecuteAlways]
    [RequireComponent(typeof(Image))]
    public class AspectRatioFiller : MonoBehaviour
    {
        private Image _image;
        private RectTransform _rt;

        private void OnEnable()
        {
            _image = GetComponent<Image>();
            _rt = GetComponent<RectTransform>();
            Apply();
        }

        private void OnRectTransformDimensionsChange()
        {
            Apply();
        }

        public void Apply()
        {
            if (_image == null || _image.sprite == null) return;

            RectTransform parentRt = _rt.parent as RectTransform;
            if (parentRt == null) return;

            Vector2 parentSize = parentRt.rect.size;
            Vector2 spriteSize = _image.sprite.rect.size;

            if (parentSize.x <= 0 || parentSize.y <= 0 || spriteSize.x <= 0 || spriteSize.y <= 0) return;

            float scale = Mathf.Max(parentSize.x / spriteSize.x, parentSize.y / spriteSize.y);
            Vector2 targetSize = spriteSize * scale;

            _rt.anchorMin = new Vector2(0.5f, 0.5f);
            _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.anchoredPosition = Vector2.zero;
            _rt.sizeDelta = targetSize;

            _image.preserveAspect = true;
        }
    }
}
