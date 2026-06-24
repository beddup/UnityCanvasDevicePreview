using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

namespace CanvasDevicePreview.Editor
{
    public class CanvasDevicePreviewWindow : EditorWindow
    {
        private const int COLUMNS = 3;

        private readonly DeviceDatabase _deviceDb = new();
        private readonly PreviewRenderer _renderer = new();
        private Dictionary<string, Vector2Int> _resolutionLookup = new();

        private Canvas _sourceCanvas;
        private readonly List<string> _activePresets = new();
        private bool _autoRefresh = true;
        private int _refreshIntervalFrames = 12;
        private int _frameCounter;
        private Vector2 _scrollPos;
        private Vector2 _leftScrollPos;
        private bool _needsRefresh;
        private float _previewHeight = 600f;
        private bool _showSelectionHighlight = true;

        private int _customW = 1080;
        private int _customH = 1920;
        private int _customNotch = 0;
        private readonly Dictionary<string, int> _customNotchHeights = new();

        // ── Adjustment state ──
        private enum HorizontalEdge { None, Left, Center, Right, Stretch }
        private enum VerticalEdge { None, Top, Center, Bottom, Stretch }
        private HorizontalEdge _horizontalEdge = HorizontalEdge.None;
        private VerticalEdge _verticalEdge = VerticalEdge.None;
        private List<GameObject> _selectedGameObjects = new();
        private List<RectTransform> _selectedRectTransforms = new();
        // Single-selection only (null when multi-selection)
        private GameObject _selectedGo;
        private RectTransform _selectedRt;
        private Image _selectedImage;
        private Button _selectedButton;
        private GameObject _selectedTextGameObject;

        [MenuItem("Window/Canvas Device Preview")]
        public static void Open()
        {
            var window = GetWindow<CanvasDevicePreviewWindow>("Canvas Preview");
            window.minSize = new Vector2(620, 420);
            window.position = new Rect(window.position.x, window.position.y, 1500, 800);
            window.Show();
        }

        // ── Lifecycle ────────────────────────────────────────────

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            Selection.selectionChanged += OnSelectionChanged;
            _deviceDb.Load();
            SeedResolutionsFromDeviceDb();
            LoadState();
            RefreshSelection();
        }

        private void OnDisable()
        {
            SaveState();
            EditorApplication.update -= Tick;
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            Selection.selectionChanged -= OnSelectionChanged;
            _renderer.DestroyAll();
        }

        private void Tick()
        {
            if (_sourceCanvas == null || !_sourceCanvas)
            {
                _renderer.DestroyAll();
                return;
            }

            if (_autoRefresh && _needsRefresh)
            {
                _frameCounter++;
                if (_frameCounter >= _refreshIntervalFrames)
                {
                    _frameCounter = 0;
                    RefreshPreviews();
                    _needsRefresh = false;
                    Repaint();
                }
            }
        }

        private void SeedResolutionsFromDeviceDb()
        {
            foreach (var device in _deviceDb.AllDevices)
                _resolutionLookup[device.FriendlyName] = device.Resolution;
        }

        private void RefreshPreviews()
        {
            var rtList = _showSelectionHighlight ? _selectedRectTransforms : null;
            _renderer.Rebuild(_sourceCanvas, _activePresets, _resolutionLookup, _deviceDb, _customNotchHeights, rtList);
            EditorUtility.SetDirty(this);
        }

        // ── Selection handling ───────────────────────────────────

        private void OnSelectionChanged()
        {
            RefreshSelection();
            RefreshPreviews();
            Repaint();
        }

        private void RefreshSelection()
        {
            var allSelected = Selection.gameObjects;

            // Filter to objects that have a RectTransform
            var validGOs = new List<GameObject>();
            var validRTs = new List<RectTransform>();

            foreach (var go in allSelected)
            {
                if (go == null) continue;
                var rt = go.GetComponent<RectTransform>();
                if (rt == null) continue;
                validGOs.Add(go);
                validRTs.Add(rt);
            }

            _selectedGameObjects = validGOs;
            _selectedRectTransforms = validRTs;

            // Single-selection fields: only populate when exactly one is selected
            if (validGOs.Count == 1)
            {
                _selectedGo = validGOs[0];
                _selectedRt = validRTs[0];
                _selectedImage = _selectedGo.GetComponent<Image>();
                _selectedButton = _selectedGo.GetComponent<Button>();
                _selectedTextGameObject = _selectedGo.GetComponent<TMP_Text>() != null ||
                                          _selectedGo.GetComponent<Text>() != null
                    ? _selectedGo
                    : null;
            }
            else
            {
                _selectedGo = null;
                _selectedRt = null;
                _selectedImage = null;
                _selectedButton = null;
                _selectedTextGameObject = null;
            }

            _horizontalEdge = HorizontalEdge.None;
            _verticalEdge = VerticalEdge.None;

            if (validRTs.Count > 0)
                DetectAnchorsFromSelection();
        }

        private void DetectAnchorsFromSelection()
        {
            if (_selectedRectTransforms.Count == 0) return;

            // Detect horizontal consistency across all selected
            HorizontalEdge firstH = DetectHorizontalEdge(_selectedRectTransforms[0]);
            bool hConsistent = firstH != HorizontalEdge.None
                && _selectedRectTransforms.All(rt => DetectHorizontalEdge(rt) == firstH);
            _horizontalEdge = hConsistent ? firstH : HorizontalEdge.None;

            // Detect vertical consistency across all selected
            VerticalEdge firstV = DetectVerticalEdge(_selectedRectTransforms[0]);
            bool vConsistent = firstV != VerticalEdge.None
                && _selectedRectTransforms.All(rt => DetectVerticalEdge(rt) == firstV);
            _verticalEdge = vConsistent ? firstV : VerticalEdge.None;
        }

        private static HorizontalEdge DetectHorizontalEdge(RectTransform rt)
        {
            Vector2 aMin = rt.anchorMin;
            Vector2 aMax = rt.anchorMax;
            // Stretch: anchorMin.x=0, anchorMax.x=1
            if (Mathf.Approximately(aMin.x, 0f) && Mathf.Approximately(aMax.x, 1f))
                return HorizontalEdge.Stretch;
            if (!Mathf.Approximately(aMin.x, aMax.x)) return HorizontalEdge.None;
            if (Mathf.Approximately(aMin.x, 0f)) return HorizontalEdge.Left;
            if (Mathf.Approximately(aMin.x, 0.5f)) return HorizontalEdge.Center;
            if (Mathf.Approximately(aMin.x, 1f)) return HorizontalEdge.Right;
            return HorizontalEdge.None;
        }

        private static VerticalEdge DetectVerticalEdge(RectTransform rt)
        {
            Vector2 aMin = rt.anchorMin;
            Vector2 aMax = rt.anchorMax;
            // Stretch: anchorMin.y=0, anchorMax.y=1
            if (Mathf.Approximately(aMin.y, 0f) && Mathf.Approximately(aMax.y, 1f))
                return VerticalEdge.Stretch;
            if (!Mathf.Approximately(aMin.y, aMax.y)) return VerticalEdge.None;
            if (Mathf.Approximately(aMin.y, 0f)) return VerticalEdge.Bottom;
            if (Mathf.Approximately(aMin.y, 0.5f)) return VerticalEdge.Center;
            if (Mathf.Approximately(aMin.y, 1f)) return VerticalEdge.Top;
            return VerticalEdge.None;
        }

        // ── Change detection ─────────────────────────────────────

        private void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (_sourceCanvas == null) return;

            for (int i = 0; i < stream.length; i++)
            {
                var type = stream.GetEventType(i);
                if (type == ObjectChangeKind.None) continue;

                if (TryGetInstanceId(stream, i, out int instanceId))
                {
                    var obj = EditorUtility.InstanceIDToObject(instanceId);
                    GameObject go = obj as GameObject ?? (obj as Component)?.gameObject;
                    if (go != null && go.transform.IsChildOf(_sourceCanvas.transform))
                    {
                        _needsRefresh = true;
                        return;
                    }
                }
                else if (type != ObjectChangeKind.CreateAssetObject
                      && type != ObjectChangeKind.DestroyAssetObject
                      && type != ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    _needsRefresh = true;
                    return;
                }
            }
        }

        private static bool TryGetInstanceId(in ObjectChangeEventStream stream, int index, out int instanceId)
        {
            switch (stream.GetEventType(index))
            {
                case ObjectChangeKind.CreateGameObjectHierarchy:
                    stream.GetCreateGameObjectHierarchyEvent(index, out var cgo); instanceId = cgo.instanceId; return true;
                case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                    stream.GetChangeGameObjectStructureHierarchyEvent(index, out var gosh); instanceId = gosh.instanceId; return true;
                case ObjectChangeKind.ChangeGameObjectStructure:
                    stream.GetChangeGameObjectStructureEvent(index, out var gos); instanceId = gos.instanceId; return true;
                case ObjectChangeKind.ChangeGameObjectParent:
                    stream.GetChangeGameObjectParentEvent(index, out var gop); instanceId = gop.instanceId; return true;
                case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    stream.GetChangeGameObjectOrComponentPropertiesEvent(index, out var gcp); instanceId = gcp.instanceId; return true;
                case ObjectChangeKind.DestroyGameObjectHierarchy:
                    stream.GetDestroyGameObjectHierarchyEvent(index, out var dgo); instanceId = dgo.instanceId; return true;
                case ObjectChangeKind.ChangeChildrenOrder:
                    stream.GetChangeChildrenOrderEvent(index, out var cco); instanceId = cco.instanceId; return true;
                case ObjectChangeKind.ChangeRootOrder:
                    stream.GetChangeRootOrderEvent(index, out var cro); instanceId = cro.instanceId; return true;
                default:
                    instanceId = 0;
                    return false;
            }
        }

        // ── Main UI ──────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.Space(6);

            float leftWidth = Mathf.Max(280f, Mathf.Min(350f, position.width * 0.32f));

            EditorGUILayout.BeginHorizontal();
            {
                // ── Left: Operations ──
                EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth));
                {
                    _leftScrollPos = EditorGUILayout.BeginScrollView(_leftScrollPos);
                    DrawSourceCanvasField();

                    if (_sourceCanvas != null)
                    {
                        EditorGUILayout.Space(4);
                        DrawDeviceDbWarning();
                        DrawDeviceSelector();
                        EditorGUILayout.Space(6);
                        DrawAdjustmentColumn();
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Select a Canvas in the scene to preview at multiple resolutions.", MessageType.Info);
                    }
                    EditorGUILayout.EndScrollView();
                }
                EditorGUILayout.EndVertical();

                GUILayout.Space(8);

                // ── Right: Preview ──
                EditorGUILayout.BeginVertical();
                {
                    DrawControls();
                    EditorGUILayout.Space(4);

                    if (_sourceCanvas == null)
                    {
                        // nothing to show
                    }
                    else if (_renderer.Slots.Count == 0 && _activePresets.Count > 0 && !_needsRefresh)
                    {
                        EditorGUILayout.HelpBox("Click 'Refresh' to generate previews.", MessageType.Info);
                    }
                    else if (_renderer.Slots.Count > 0)
                    {
                        float rightWidth = position.width - leftWidth - 30f;
                        DrawPreviewGrid(rightWidth);
                    }
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSourceCanvasField()
        {
            EditorGUILayout.LabelField("Source Canvas", EditorStyles.boldLabel);
            var newCanvas = (Canvas)EditorGUILayout.ObjectField(_sourceCanvas, typeof(Canvas), true);
            if (newCanvas == null)
            {
                newCanvas = GameObject.FindAnyObjectByType<Canvas>();
            }
            if (newCanvas != _sourceCanvas)
            {
                _sourceCanvas = newCanvas;
                _renderer.DestroyAll();
                _needsRefresh = true;
            }
        }

        private void DrawDeviceDbWarning()
        {
            if (!_deviceDb.IsLoaded || _deviceDb.AllDevices.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Device Simulator Devices package not found. Install 'com.unity.device-simulator.devices' "
                    + "for device presets. Custom resolutions are available below.",
                    MessageType.Warning);
            }
        }

        // ── Adjustment Column ────────────────────────────────────

        private void DrawAdjustmentColumn()
        {
            EditorGUILayout.BeginVertical("box");
            {
                string label;
                if (_selectedRectTransforms.Count == 0)
                    label = "未选择任何RectTransform";
                else if (_selectedRectTransforms.Count == 1)
                    label = $"已选择 {_selectedGameObjects[0].name}";
                else
                    label = $"已选择 {_selectedRectTransforms.Count} 个 RectTransform";

                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

                if (_selectedRectTransforms.Count == 0)
                {
                    EditorGUILayout.HelpBox("Select a GameObject in the Canvas to adjust layout, image, or button properties.", MessageType.Info);
                    EditorGUILayout.EndVertical();
                    return;
                }

                DrawLayoutSection();
                GUILayout.Space(8);
                // Image / Button / Text tools are single-selection only
                if (_selectedImage != null) DrawImageSection();
                GUILayout.Space(8);
                if (_selectedButton != null) DrawButtonSection();
                GUILayout.Space(8);
                if (_selectedTextGameObject != null) DrawTextSection();
            }
            EditorGUILayout.EndVertical();
        }

        // ── Adjustment: Layout ───────────────────────────────────

        private void DrawLayoutSection()
        {
            EditorGUILayout.LabelField("布局", EditorStyles.boldLabel);

            // Verify all selected RectTransforms have a parent
            bool allHaveParents = _selectedRectTransforms.All(rt => rt != null && rt.parent is RectTransform);
            if (!allHaveParents)
            {
                EditorGUILayout.HelpBox("部分选中的对象没有父 RectTransform。", MessageType.Warning);
                return;
            }

            // ── 横向 ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("横向", EditorStyles.miniBoldLabel, GUILayout.Width(36));
            DrawHorizontalButton("靠左", HorizontalEdge.Left);
            DrawHorizontalButton("居中", HorizontalEdge.Center);
            DrawHorizontalButton("靠右", HorizontalEdge.Right);
            DrawHorizontalButton("左右对齐", HorizontalEdge.Stretch);
            EditorGUILayout.EndHorizontal();

            // ── 纵向 ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("纵向", EditorStyles.miniBoldLabel, GUILayout.Width(36));
            DrawVerticalButton("靠顶", VerticalEdge.Top);
            DrawVerticalButton("居中", VerticalEdge.Center);
            DrawVerticalButton("靠底", VerticalEdge.Bottom);
            DrawVerticalButton("上下对齐", VerticalEdge.Stretch);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawHorizontalButton(string label, HorizontalEdge edge)
        {
            Color oldBg = GUI.backgroundColor;
            if (_horizontalEdge == edge)
                GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
            if (GUILayout.Button(label))
                ApplyHorizontalAnchor(edge);
            GUI.backgroundColor = oldBg;
        }

        private void DrawVerticalButton(string label, VerticalEdge edge)
        {
            Color oldBg = GUI.backgroundColor;
            if (_verticalEdge == edge)
                GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
            if (GUILayout.Button(label))
                ApplyVerticalAnchor(edge);
            GUI.backgroundColor = oldBg;
        }

        private void ApplyHorizontalAnchor(HorizontalEdge edge)
        {
            if (_selectedRectTransforms.Count == 0) return;

            foreach (var rt in _selectedRectTransforms)
            {
                if (rt == null) continue;
                RectTransform parentRt = rt.parent as RectTransform;
                if (parentRt == null) continue;

                Undo.RecordObject(rt, $"Set Anchor H:{edge}");

                Vector2 parentSize = parentRt.rect.size;
                Vector2 posMin = Vector2.Scale(rt.anchorMin, parentSize) + rt.offsetMin;
                Vector2 posMax = Vector2.Scale(rt.anchorMax, parentSize) + rt.offsetMax;

                if (edge == HorizontalEdge.Stretch)
                {
                    rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
                    rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
                }
                else
                {
                    float ax;
                    switch (edge)
                    {
                        case HorizontalEdge.Left:   ax = 0f; break;
                        case HorizontalEdge.Center: ax = 0.5f; break;
                        case HorizontalEdge.Right:  ax = 1f; break;
                        default: continue;
                    }
                    rt.anchorMin = new Vector2(ax, rt.anchorMin.y);
                    rt.anchorMax = new Vector2(ax, rt.anchorMax.y);
                }

                rt.offsetMin = posMin - Vector2.Scale(rt.anchorMin, parentSize);
                rt.offsetMax = posMax - Vector2.Scale(rt.anchorMax, parentSize);

                EditorUtility.SetDirty(rt);
            }

            _horizontalEdge = edge;
            Repaint();
        }

        private void ApplyVerticalAnchor(VerticalEdge edge)
        {
            if (_selectedRectTransforms.Count == 0) return;

            foreach (var rt in _selectedRectTransforms)
            {
                if (rt == null) continue;
                RectTransform parentRt = rt.parent as RectTransform;
                if (parentRt == null) continue;

                Undo.RecordObject(rt, $"Set Anchor V:{edge}");

                Vector2 parentSize = parentRt.rect.size;
                Vector2 posMin = Vector2.Scale(rt.anchorMin, parentSize) + rt.offsetMin;
                Vector2 posMax = Vector2.Scale(rt.anchorMax, parentSize) + rt.offsetMax;

                if (edge == VerticalEdge.Stretch)
                {
                    rt.anchorMin = new Vector2(rt.anchorMin.x, 0f);
                    rt.anchorMax = new Vector2(rt.anchorMax.x, 1f);
                }
                else
                {
                    float ay;
                    switch (edge)
                    {
                        case VerticalEdge.Top:    ay = 1f; break;
                        case VerticalEdge.Center: ay = 0.5f; break;
                        case VerticalEdge.Bottom: ay = 0f; break;
                        default: continue;
                    }
                    rt.anchorMin = new Vector2(rt.anchorMin.x, ay);
                    rt.anchorMax = new Vector2(rt.anchorMax.x, ay);
                }

                rt.offsetMin = posMin - Vector2.Scale(rt.anchorMin, parentSize);
                rt.offsetMax = posMax - Vector2.Scale(rt.anchorMax, parentSize);

                EditorUtility.SetDirty(rt);
            }

            _verticalEdge = edge;
            Repaint();
        }

        // ── Adjustment: Image ────────────────────────────────────

        private void DrawImageSection()
        {
            Sprite sprite = _selectedImage.sprite;
            if (sprite == null)
            {
                EditorGUILayout.HelpBox("未指定 Sprite。", MessageType.Warning);
                return;
            }

            Rect rect = sprite.rect;
            float spriteW = rect.width;
            float spriteH = rect.height;
            float rtW = _selectedRt.rect.width;
            float rtH = _selectedRt.rect.height;
            bool hasBorder = sprite.border.x > 0 || sprite.border.y > 0
                          || sprite.border.z > 0 || sprite.border.w > 0;

            bool spriteIsSmaller = spriteW < rtW || spriteH < rtH;
            bool alreadySlicedOrTiled = _selectedImage.type == Image.Type.Sliced
                                     || _selectedImage.type == Image.Type.Tiled;
            bool hasAspectRatioFiller = _selectedGo.TryGetComponent<AspectRatioFiller>(out _);

            // 1. Title + size info
            EditorGUILayout.LabelField($"图片 (Sprite: {spriteW:F0}x{spriteH:F0}, RT: {rtW:F0}x{rtH:F0})", EditorStyles.boldLabel);

            // 2. Current fill type
            string fillInfo = $"填充方式: {_selectedImage.type}";
            if (hasAspectRatioFiller)
                fillInfo += "  |  保持比例铺满屏幕";
            EditorGUILayout.LabelField(fillInfo);

            // 3. Conditional warning / error
            if (spriteIsSmaller && !alreadySlicedOrTiled)
            {
                EditorGUILayout.HelpBox(
                    "请检查是否应使用Sliced或Tiled。",
                    MessageType.Warning);
            }
            else if (alreadySlicedOrTiled && !hasBorder)
            {
                EditorGUILayout.HelpBox(
                    "当前 Sprite 没有设置 Border。需在 Sprite Editor 中设置 Border 才能正确拉伸。",
                    MessageType.Error);

                if (GUILayout.Button("设置 Border"))
                    OpenSpriteEditor();
            }

            // 4. Button: 设置 9-Slice
            if (GUILayout.Button("设置 9-Slice"))
            {
                Undo.RecordObject(_selectedImage, "Set Image Type Sliced");
                _selectedImage.type = Image.Type.Sliced;
                EditorUtility.SetDirty(_selectedImage);
                if (!hasBorder)
                    OpenSpriteEditor();
            }

            // 5. Button: Shrink sliced sprite
            if (_selectedImage.type == Image.Type.Sliced && GUILayout.Button("Shrink"))
            {
                ShrinkSlicedSprite.ShrinkSprite(_selectedImage.sprite);
            }

            // 6. Button: 保持比例铺满屏幕
            if (!hasAspectRatioFiller && GUILayout.Button("保持比例铺满屏幕"))
            {
                Undo.AddComponent<AspectRatioFiller>(_selectedGo);
            }
        }

        private void OpenSpriteEditor()
        {
            UnityEngine.Object restoreSelection = _selectedGo;
            if (_selectedImage != null && _selectedImage.sprite != null)
            {
                Selection.activeObject = _selectedImage.sprite;
                EditorGUIUtility.PingObject(_selectedImage.sprite);
            }
            EditorApplication.ExecuteMenuItem("Window/2D/Sprite Editor");
            EditorApplication.delayCall += () =>
            {
                if (restoreSelection != null)
                    Selection.activeObject = restoreSelection;
            };
        }

        // ── Adjustment: Button ───────────────────────────────────

        private void DrawButtonSection()
        {
            Transform existing = _selectedGo.transform.Find("ClickArea");
            float w, h;

            if (existing != null && existing.TryGetComponent<RectTransform>(out var clickRt))
            {
                w = clickRt.rect.width;
                h = clickRt.rect.height;
            }
            else
            {
                w = _selectedRt.rect.width;
                h = _selectedRt.rect.height;
            }

            EditorGUILayout.LabelField($"按钮(点击区域大小 {w:F0} x {h:F0})", EditorStyles.boldLabel);

            if (existing != null)
            {
                EditorGUILayout.LabelField("  已有 ClickArea。", EditorStyles.miniLabel);
            }
            else
            {
                if (GUILayout.Button("增加点击区域"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(_selectedGo, "Create ClickArea");
                    CreateClickAreaChild();
                    EditorUtility.SetDirty(_selectedGo);
                }
            }
        }

        private void CreateClickAreaChild()
        {
            GameObject clickArea = new GameObject("ClickArea");
            Undo.RegisterCreatedObjectUndo(clickArea, "Create ClickArea");

            clickArea.transform.SetParent(_selectedGo.transform, false);
            clickArea.transform.localScale = Vector3.one;
            clickArea.transform.SetAsFirstSibling();
            clickArea.layer = _selectedGo.layer;

            RectTransform rt = clickArea.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(120f, 120f);
            rt.anchoredPosition = Vector2.zero;

            Image img = clickArea.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = true;
        }

        // ── Adjustment: Text ─────────────────────────────────────

        private void DrawTextSection()
        {
            EditorGUILayout.LabelField("文字", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "文字内容在运行时可能会变化，请确认以下设置：\n" +
                "  1. 文字显示范围\n" +
                "  2. 文字对齐方式、自动大小等设置",
                MessageType.Warning);
        }

        // ── Device Selector ──────────────────────────────────────

        private void DrawDeviceSelector()
        {
            EditorGUILayout.BeginVertical("box");
            {
                EditorGUILayout.LabelField("Target Resolutions", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();

                var btnRect = GUILayoutUtility.GetRect(new GUIContent("Select Devices..."), EditorStyles.miniButton, GUILayout.Width(140));
                if (GUI.Button(btnRect, "Select Devices...", EditorStyles.miniButton))
                    ShowDeviceMenu(btnRect);

                if (GUILayout.Button("Clear All", GUILayout.Width(70)))
                {
                    _activePresets.Clear();
                    ApplyStateChange();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField("w", GUILayout.Width(12));
                _customW = EditorGUILayout.IntField(_customW, GUILayout.Width(44));
                GUILayout.Space(4);

                EditorGUILayout.LabelField("h", GUILayout.Width(12));
                _customH = EditorGUILayout.IntField(_customH, GUILayout.Width(44));
                GUILayout.Space(4);

                EditorGUILayout.LabelField("notch", GUILayout.Width(40));
                _customNotch = EditorGUILayout.IntField(_customNotch, GUILayout.Width(38));

                if (GUILayout.Button("Add", GUILayout.Width(60)))
                {
                    string key = $"Custom {_customW}x{_customH} Notch{_customNotch}";
                    _resolutionLookup[key] = new Vector2Int(_customW, _customH);
                    _customNotchHeights[key] = _customNotch;
                    if (!_activePresets.Contains(key))
                        _activePresets.Add(key);
                    ApplyStateChange();
                }

                EditorGUILayout.EndHorizontal();

                if (_activePresets.Count > 0)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Active Devices", EditorStyles.miniBoldLabel);
                    foreach (var key in _activePresets)
                    {
                        Vector2Int res = _resolutionLookup.TryGetValue(key, out var r) ? r : Vector2Int.zero;
                        int notch = _customNotchHeights.TryGetValue(key, out var nh) ? nh : 0;
                        string notchStr = notch > 0 ? $"  notch:{notch}" : "";

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"{key}  {res.x}x{res.y}{notchStr}", EditorStyles.miniLabel);
                        if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(14)))
                        {
                            _activePresets.Remove(key);
                            ApplyStateChange();
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void ShowDeviceMenu(Rect buttonRect)
        {
            var menu = new GenericMenu();
            var grouped = _deviceDb.GroupedByBrand;
            var sortedBrands = grouped.Keys.OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (string brand in sortedBrands)
            {
                foreach (var device in grouped[brand])
                {
                    string path = $"{brand}/{device.FriendlyName} ({device.Resolution.x}x{device.Resolution.y})";
                    bool on = _activePresets.Contains(device.FriendlyName);
                    string key = device.FriendlyName;
                    menu.AddItem(new GUIContent(path), on, () => ToggleDevice(key));
                }
            }

            if (sortedBrands.Count == 0)
                menu.AddDisabledItem(new GUIContent("No devices found"));

            menu.DropDown(buttonRect);
        }

        private void ToggleDevice(string deviceName)
        {
            if (_activePresets.Contains(deviceName))
                _activePresets.Remove(deviceName);
            else
                _activePresets.Add(deviceName);
            ApplyStateChange();
        }


        // ── Controls ─────────────────────────────────────────────

        private void DrawControls()
        {
            EditorGUILayout.BeginHorizontal();
            _autoRefresh = EditorGUILayout.ToggleLeft("Auto Refresh", _autoRefresh, GUILayout.Width(100));
            float secs = _refreshIntervalFrames / 60f;
            secs = EditorGUILayout.Slider(secs, 0.2f, 5f, GUILayout.Width(60));
            _refreshIntervalFrames = Mathf.Max(1, Mathf.RoundToInt(secs * 60f));
            EditorGUILayout.LabelField($"{secs:F1}s", GUILayout.Width(30));
            if (GUILayout.Button("Refresh", GUILayout.Width(80))) { RefreshPreviews(); _needsRefresh = false; }
            GUILayout.Space(20);

            EditorGUILayout.LabelField("Preview Height:", GUILayout.Width(100));
            _previewHeight = EditorGUILayout.Slider(_previewHeight, 300, 2000, GUILayout.Width(60));

            GUILayout.Space(20);
            var oldShow = _showSelectionHighlight;
            _showSelectionHighlight = EditorGUILayout.ToggleLeft("Show Selection", _showSelectionHighlight, GUILayout.Width(110));
            if (_showSelectionHighlight != oldShow)
                _needsRefresh = true;

            EditorGUILayout.EndHorizontal();
        }

        // ── Preview Grid ─────────────────────────────────────────

        private void DrawPreviewGrid(float availWidth)
        {
            float colW = (availWidth - (COLUMNS - 1) * 6f) / COLUMNS;

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            var slots = _renderer.Slots;

            for (int i = 0; i < slots.Count; i++)
            {
                if (i % COLUMNS == 0)
                    EditorGUILayout.BeginHorizontal();

                DrawPreview(slots[i], colW);

                if (i % COLUMNS == COLUMNS - 1 || i == slots.Count - 1)
                {
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(8);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawPreview(PreviewSlot p, float maxWidth)
        {
            float displayW = maxWidth - 8f;
            float displayH = _previewHeight;
            float screenDrawW = 0f;
            float screenDrawH = 0f;
            float overlayScale = 0f;

            if (p.RenderTexture != null)
            {
                float rtW = p.RenderTexture.width;
                float rtH = p.RenderTexture.height;

                if (p.OverlayTexture != null)
                {
                    float osw = p.OverlayTexture.width - p.BorderSize.x - p.BorderSize.z;
                    float osh = p.OverlayTexture.height - p.BorderSize.y - p.BorderSize.w;

                    if (osw > 0 && osh > 0)
                    {
                        overlayScale = Mathf.Min(displayW / p.OverlayTexture.width, displayH / p.OverlayTexture.height);
                        screenDrawW = osw * overlayScale;
                        screenDrawH = osh * overlayScale;
                    }
                    else
                    {
                        float scale = Mathf.Min(displayW / rtW, displayH / rtH);
                        screenDrawW = rtW * scale;
                        screenDrawH = rtH * scale;
                    }
                }
                else
                {
                    float scale = Mathf.Min(displayW / rtW, displayH / rtH);
                    screenDrawW = rtW * scale;
                    screenDrawH = rtH * scale;
                }
            }

            float boxH = displayH + 33f;
            EditorGUILayout.BeginVertical("box", GUILayout.Width(maxWidth), GUILayout.Height(boxH));

            // ── header: label + close ──
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{p.Label}  {p.Resolution.x}x{p.Resolution.y}", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.9f, 0.35f, 0.35f);
            if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(15)))
            {
                _activePresets.Remove(p.Key);
                GUI.backgroundColor = oldBg;
                ApplyStateChange();
                GUIUtility.ExitGUI();
            }
            GUI.backgroundColor = oldBg;
            EditorGUILayout.EndHorizontal();

            // ── notch info ──
            GUILayout.Space(-5);
            EditorGUILayout.LabelField($"Device Notch: {p.DeviceNotchHeight} (Canvas Notch : {p.CanvasNotchHeight})", EditorStyles.miniLabel);

            if (p.RenderTexture != null && screenDrawW > 0)
            {
                GUILayout.FlexibleSpace();
                Rect r = GUILayoutUtility.GetRect(screenDrawW, screenDrawH, GUILayout.ExpandWidth(false));
                r.x += (maxWidth - screenDrawW) * 0.5f;

                GUI.DrawTexture(r, p.RenderTexture, ScaleMode.ScaleToFit);

                if (p.DeviceNotchHeight > 0 && p.OverlayTexture == null)
                {
                    float notchH = p.DeviceNotchHeight * (screenDrawH / p.Resolution.y);
                    var notchRect = new Rect(r.x, r.y, r.width, notchH);
                    EditorGUI.DrawRect(notchRect, new Color(0f, 0.0f, 0.0f, 0.7f));
                }

                if (p.OverlayTexture != null && overlayScale > 0)
                {
                    float overlayX = r.x - p.BorderSize.x * overlayScale;
                    float overlayY = r.y - p.BorderSize.y * overlayScale;
                    float overlayW = p.OverlayTexture.width * overlayScale;
                    float overlayH = p.OverlayTexture.height * overlayScale;

                    GUI.DrawTexture(new Rect(overlayX, overlayY, overlayW, overlayH),
                        p.OverlayTexture, ScaleMode.ScaleToFit);
                }

                GUILayout.FlexibleSpace();
            }
            else
            {
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.EndVertical();
        }

        // ── Persistence ──────────────────────────────────────────

        private const string PREFS_KEY = "CanvasDevicePreview.State";
        private const string LEGACY_PREFS_KEY = "Multi" + "ResolutionPreview.State";

        [Serializable]
        private class SavedState
        {
            public List<string> activePresets = new();
            public List<CustomResEntry> customResolutions = new();
        }

        [Serializable]
        private class CustomResEntry
        {
            public string key;
            public int w;
            public int h;
            public int notch;
        }

        private void ApplyStateChange()
        {
            SaveState();
            RefreshPreviews();
            _needsRefresh = false;
            Repaint();
        }

        private void SaveState()
        {
            var state = new SavedState();
            foreach (var p in _activePresets)
                state.activePresets.Add(p);
            foreach (var kv in _resolutionLookup)
            {
                if (kv.Key.StartsWith("Custom "))
                {
                    int notch = _customNotchHeights.TryGetValue(kv.Key, out var nh) ? nh : 0;
                    state.customResolutions.Add(new CustomResEntry { key = kv.Key, w = kv.Value.x, h = kv.Value.y, notch = notch });
                }
            }

            EditorPrefs.SetString(PREFS_KEY, JsonUtility.ToJson(state));
        }

        private void LoadState()
        {
            var json = EditorPrefs.GetString(PREFS_KEY, "");
            if (string.IsNullOrEmpty(json))
                json = EditorPrefs.GetString(LEGACY_PREFS_KEY, "");
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var state = JsonUtility.FromJson<SavedState>(json);
                if (state?.activePresets != null)
                {
                    foreach (var preset in state.activePresets)
                        _activePresets.Add(preset);
                }

                if (state?.customResolutions != null)
                {
                    foreach (var cr in state.customResolutions)
                    {
                        _resolutionLookup[cr.key] = new Vector2Int(cr.w, cr.h);
                        _customNotchHeights[cr.key] = cr.notch;
                        _customW = cr.w;
                        _customH = cr.h;
                        _customNotch = cr.notch;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CanvasDevicePreview] Failed to load saved state: {e.Message}");
            }
        }
    }
}
