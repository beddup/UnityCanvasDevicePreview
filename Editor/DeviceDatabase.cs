using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor.PackageManager;

namespace CanvasDevicePreview.Editor
{
    /// <summary>
    /// Loads device definitions from the com.unity.device-simulator.devices package.
    /// Provides resolution lookup and overlay texture loading.
    /// </summary>
    public class DeviceDatabase
    {
        public IReadOnlyList<DeviceDef> AllDevices => _allDevices;
        public IReadOnlyDictionary<string, List<DeviceDef>> GroupedByBrand => _groupedByBrand;
        public bool IsLoaded { get; private set; }

        private readonly List<DeviceDef> _allDevices = new();
        private readonly Dictionary<string, List<DeviceDef>> _groupedByBrand = new();
        private string _packagePath;

        public void Load()
        {
            if (IsLoaded) return;

            _allDevices.Clear();
            _groupedByBrand.Clear();

            var packageInfo = PackageInfo.FindForPackageName("com.unity.device-simulator.devices");
            if (packageInfo != null)
            {
                _packagePath = packageInfo.resolvedPath;
                string devicesDir = Path.Combine(_packagePath, "Editor", "Devices");
                if (Directory.Exists(devicesDir))
                    LoadDevicesFrom(devicesDir, devicesDir);
            }

            string localPath = Path.Combine(Application.dataPath, "CanvasDevicePreview/Editor/Devices");
            if (Directory.Exists(localPath))
                LoadDevicesFrom(localPath, localPath);

            foreach (var list in _groupedByBrand.Values)
                list.Sort((a, b) => string.Compare(a.FriendlyName, b.FriendlyName, StringComparison.OrdinalIgnoreCase));

            IsLoaded = true;
        }

        private void LoadDevicesFrom(string dirPath, string overlayBasePath)
        {
            foreach (string filePath in Directory.GetFiles(dirPath, "*.device"))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    var def = JsonUtility.FromJson<DeviceDefinitionJson>(json);
                    if (def == null || def.screens == null || def.screens.Length == 0) continue;

                    var screen = def.screens[0];
                    if (screen.width <= 0 || screen.height <= 0) continue;

                    string brand = GetBrand(def.friendlyName);
                    string overlayPath = null;
                    Vector4 borderSize = default;
                    if (screen.presentation != null && !string.IsNullOrEmpty(screen.presentation.overlayPath))
                    {
                        overlayPath = screen.presentation.overlayPath;
                        borderSize = screen.presentation.borderSize;
                    }

                    // local overrides package
                    int existingIdx = _allDevices.FindIndex(d => d.FriendlyName == def.friendlyName);
                    if (existingIdx >= 0)
                        _allDevices.RemoveAt(existingIdx);

                    var device = new DeviceDef
                    {
                        FriendlyName = def.friendlyName,
                        Brand = brand,
                        Resolution = new Vector2Int(screen.width, screen.height),
                        OverlayPath = overlayPath,
                        OverlayBasePath = overlayBasePath,
                        BorderSize = borderSize,
                        NotchHeight = ComputeNotchHeight(screen)
                    };

                    _allDevices.Add(device);

                    if (!_groupedByBrand.ContainsKey(brand))
                        _groupedByBrand[brand] = new List<DeviceDef>();
                    else if (existingIdx >= 0)
                        _groupedByBrand[brand].RemoveAll(d => d.FriendlyName == def.friendlyName);
                    _groupedByBrand[brand].Add(device);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DeviceDB] Failed to parse {Path.GetFileName(filePath)}: {e.Message}");
                }
            }
        }

        public bool TryGetDevice(string name, out DeviceDef device)
        {
            for (int i = 0; i < _allDevices.Count; i++)
            {
                if (_allDevices[i].FriendlyName == name)
                {
                    device = _allDevices[i];
                    return true;
                }
            }
            device = default;
            return false;
        }

        public Texture2D LoadOverlayTexture(string overlayPath, string overlayBasePath)
        {
            string fullPath = Path.Combine(overlayBasePath, overlayPath);
            if (!File.Exists(fullPath)) return null;

            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                tex.LoadImage(File.ReadAllBytes(fullPath));
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DeviceDB] Failed to load overlay {overlayPath}: {e.Message}");
                return null;
            }
        }

        private static int ComputeNotchHeight(ScreenJson screen)
        {
            if (screen.orientations == null) return 0;
            foreach (var o in screen.orientations)
            {
                if (o.orientation == 1)
                    return (int)Mathf.Max(0, screen.height - o.safeArea.yMax);
            }
            return 0;
        }

        private static string GetBrand(string friendlyName)
        {
            int spaceIdx = friendlyName.IndexOf(' ');
            string brand = (spaceIdx > 0) ? friendlyName.Substring(0, spaceIdx) : friendlyName;
            if (brand == "LGE") brand = "LG";
            return brand;
        }

        // ── JSON types ──

        [Serializable]
        private class DeviceDefinitionJson
        {
            public string friendlyName;
            public ScreenJson[] screens;
        }

        [Serializable]
        private class ScreenJson
        {
            public int width;
            public int height;
            public PresentationJson presentation;
            public OrientationJson[] orientations;
        }

        [Serializable]
        private class OrientationJson
        {
            public int orientation;
            public Rect safeArea;
        }

        [Serializable]
        private class PresentationJson
        {
            public string overlayPath;
            public Vector4 borderSize;
        }
    }

    public struct DeviceDef
    {
        public string FriendlyName;
        public string Brand;
        public Vector2Int Resolution;
        public string OverlayPath;
        public string OverlayBasePath;
        public Vector4 BorderSize;
        public int NotchHeight;
    }
}
