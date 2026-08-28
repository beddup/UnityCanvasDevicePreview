using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

namespace CanvasDevicePreview.Editor
{
    public partial class CanvasDevicePreviewWindow
    {
        // ── Play Mode 预览源 ────────────────────────────────────────────────
        // Play Mode 下直接 Instantiate 源 Canvas 会触发业务脚本 Awake 副作用（弹广告、改状态等），
        // 所以把源 Canvas 存成临时 prefab → LoadPrefabContents 拿到可编辑副本 → 删掉业务脚本/
        // GraphicRaycaster → 作为渲染源交给 PreviewRenderer 实例化。副本不写回文件，用完卸载+删除。
        // 动态加载的资源（bundle Sprite / Spine 骨架数据等）无法写进 prefab，实例化前从源补回引用。

        private string _playModeTempPath;
        private GameObject _playModeStrippedRoot;

        // 非 Play Mode（或源为空）返回 null；Play Mode 返回「删过业务脚本的可编辑副本」上的 Canvas。
        private Canvas AcquirePlayModeSource(Canvas sourceCanvas)
        {
            if (!Application.isPlaying || sourceCanvas == null) return null;

            _playModeTempPath = "Assets/__CDP_Preview.prefab";
            _playModeStrippedRoot = BuildStrippedPrefab(sourceCanvas, _playModeTempPath);
            return _playModeStrippedRoot != null
                ? _playModeStrippedRoot.GetComponent<Canvas>()
                : null;
        }

        // 每次刷新后调用：卸载可编辑副本 + 删除临时 prefab（未获取时为空操作）。
        private void ReleasePlayModeSource()
        {
            if (_playModeStrippedRoot != null)
                PrefabUtility.UnloadPrefabContents(_playModeStrippedRoot);
            _playModeStrippedRoot = null;

            if (!string.IsNullOrEmpty(_playModeTempPath))
                AssetDatabase.DeleteAsset(_playModeTempPath);
            _playModeTempPath = null;
        }

        // 把源 Canvas 存成临时 prefab，再 LoadPrefabContents 拿到可编辑副本，删掉业务脚本后直接返回副本。
        // 注意：副本与 prefab 文件/资产无连接，这里不写回（省一次磁盘写）；调用方负责 ReleasePlayModeSource。
        private static GameObject BuildStrippedPrefab(Canvas sourceCanvas, string tempPath)
        {
            PrefabUtility.SaveAsPrefabAsset(sourceCanvas.gameObject, tempPath);

            var contentsRoot = PrefabUtility.LoadPrefabContents(tempPath);
            try
            {
                foreach (var c in contentsRoot.GetComponentsInChildren<Component>(true))
                {
                    if (ShouldRemoveComponent(c))
                        UnityEngine.Object.DestroyImmediate(c);
                }
                // 预览克隆不拦截射线
                foreach (var g in contentsRoot.GetComponentsInChildren<Graphic>(true))
                    g.raycastTarget = false;

                // 动态加载的资源（bundle 里的 Sprite/Texture/Material 等）无法写进 prefab 文件，
                // 副本里对应字段为 null；实例化前从源 Canvas 补回（内存态），clone 组件 Awake 时
                // 就能读到正确数据（如 SkeletonGraphic 需要骨架数据初始化、Image 需要 sprite 渲染）。
                PatchNullReferences(sourceCanvas.gameObject, contentsRoot);

                return contentsRoot;
            }
            catch
            {
                // 构建失败时卸载编辑副本，避免隐藏在编辑场景里的对象泄漏
                PrefabUtility.UnloadPrefabContents(contentsRoot);
                throw;
            }
        }

        // 把 target 树（临时 prefab 的可编辑副本）上因「动态资源无法写入 prefab 文件」而变空的引用字段，
        // 从源 Canvas 对应字段补回。必须在实例化前做——这样 clone 组件 Awake 时就能读到正确数据
        // （如 SkeletonGraphic 需要骨架数据初始化、Image 需要 sprite 渲染）。
        private static int PatchNullReferences(GameObject sourceRoot, GameObject targetRoot)
        {
            var sourceTransform = sourceRoot.transform;
            var targetTransform = targetRoot.transform;
            int patched = 0;

            foreach (var targetComp in targetRoot.GetComponentsInChildren<Component>(true))
            {
                if (targetComp == null) continue;
                var srcComp = FindCorrespondingComponent(targetComp, targetTransform, sourceTransform);
                if (srcComp == null) continue;

                var so = new SerializedObject(targetComp);
                var srcSo = new SerializedObject(srcComp);
                var p = so.GetIterator();
                bool changed = false;
                while (p.Next(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (p.objectReferenceValue != null) continue; // 已正确序列化

                    var srcProp = srcSo.FindProperty(p.propertyPath);
                    if (srcProp == null) continue;
                    var srcValue = srcProp.objectReferenceValue;
                    if (srcValue == null) continue;

                    var mapped = MapReferenceValue(srcValue, sourceTransform, targetTransform);
                    if (mapped == null) continue;

                    p.objectReferenceValue = mapped;
                    changed = true;
                    patched++;
                }
                if (changed)
                    so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (patched > 0)
                Debug.Log($"[CanvasDevicePreview] 实例化前从源 Canvas 补回 {patched} 个引用到临时 prefab");
            return patched;
        }

        // 把源引用值映射成 target 树（临时 prefab）里可用的对象：
        // - 源树内的 Component/GameObject → 映射到 target 中对应对象；找不到（运行时新建对象）返回 null
        // - 动态加载的资产（bundle 里的 Sprite/Texture/Material 等）→ 直接返回原运行时引用
        private static UnityEngine.Object MapReferenceValue(UnityEngine.Object srcValue, Transform sourceTransform, Transform targetTransform)
        {
            if (srcValue is Component comp)
                return FindCorrespondingComponent(comp, sourceTransform, targetTransform);

            if (srcValue is GameObject go)
            {
                var path = GetTransformPath(go.transform, sourceTransform);
                return path == null ? null : FindGameObjectByPath(targetTransform, path);
            }

            return srcValue;
        }

        // 在 toRoot 树下找与 comp 同路径、同类型、同类型内索引的组件。
        private static Component FindCorrespondingComponent(Component comp, Transform fromRoot, Transform toRoot)
        {
            var path = GetTransformPath(comp.transform, fromRoot);
            if (path == null) return null;

            var toGo = FindGameObjectByPath(toRoot, path);
            if (toGo == null) return null;

            var type = comp.GetType();
            var fromComps = comp.gameObject.GetComponents(type);
            var toComps = toGo.GetComponents(type);
            int idx = Array.IndexOf(fromComps, comp);
            if (idx < 0 || idx >= toComps.Length) return null;
            return toComps[idx] as Component;
        }

        // 从 root 到 t 的兄弟索引路径（如 "0/2/1"）；t 不在 root 树下返回 null。
        private static string GetTransformPath(Transform t, Transform root)
        {
            var parts = new List<int>();
            Transform cur = t;
            while (cur != null && cur != root)
            {
                parts.Add(cur.GetSiblingIndex());
                cur = cur.parent;
            }
            if (cur != root) return null;

            parts.Reverse();
            return string.Join("/", parts);
        }

        // 按兄弟索引路径在 root 树下定位 GameObject。
        private static GameObject FindGameObjectByPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root.gameObject;
            Transform cur = root;
            foreach (var s in path.Split('/'))
            {
                if (!int.TryParse(s, out int idx)) return null;
                if (idx < 0 || idx >= cur.childCount) return null;
                cur = cur.GetChild(idx);
            }
            return cur.gameObject;
        }

        // 删除标准：GraphicRaycaster、自定义 MonoBehaviour（非 uGUI/TMP/Spine、非设备预览消息处理器）
        private static bool ShouldRemoveComponent(Component c)
        {
            if (c is GraphicRaycaster) return true;
            if (!(c is MonoBehaviour)) return false;
            
            string ns = c.GetType().Namespace ?? "";
            bool isKnown = ns == "UnityEngine.UI" || ns.StartsWith("UnityEngine.UI.")
                        || ns == "TMPro"        || ns.StartsWith("TMPro.")
                        || ns == "Spine"        || ns.StartsWith("Spine.");
            if (isKnown) return false;
            if (HasDeviceNotchSimulationHandler(c)) return false;
            return true;
        }

        private static bool HasDeviceNotchSimulationHandler(Component c)
        {
            return c.GetType().GetMethod(
                CanvasDevicePreviewMessages.SimulateDevice,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                null,
                new[] { typeof(Dictionary<string, object>) },
                null) != null;
        }
    }
}
