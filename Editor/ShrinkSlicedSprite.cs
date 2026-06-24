using UnityEngine;
using UnityEditor;
using System.IO;

namespace CanvasDevicePreview.Editor
{
    public class ShrinkSlicedSprite
    {
        private const string BackupRoot = "Library/ShrinkSlicedSprites";
        private const string ShrinkMenuPath = "Assets/Shrink Sliced Sprite";
        private const string UndoShrinkMenuPath = "Assets/Undo Shrink Sliced Sprite";

        [MenuItem(ShrinkMenuPath)]
        public static void ShrinkSelectedSprite()
        {
            if (!TryGetSelectedSprite(out var theSprite))
                return;

            ShrinkSprite(theSprite);
        }

        [MenuItem(UndoShrinkMenuPath)]
        public static void RestoreSelectedSprite()
        {
            if (!TryGetSelectedSprite(out var theSprite))
                return;

            var selectedPath = AssetDatabase.GetAssetPath(theSprite);
            string backupPath = GetBackupPath(selectedPath);
            if (!File.Exists(backupPath))
            {
                Debug.LogWarning($"[ShrinkSlicedSprite] Backup file not found: {backupPath}");
                return;
            }

            File.Copy(backupPath, selectedPath, true);
            AssetDatabase.ImportAsset(selectedPath);
            Debug.Log($"[ShrinkSlicedSprite] Restored sprite from backup: {backupPath}");
        }

        /// <summary>
        /// Shrink a sliced sprite's texture so that its dimensions are reduced
        /// to just the border (corner) regions plus padding to a multiple of 4.
        /// </summary>
        public static void ShrinkSprite(Sprite theSprite)
        {
            if (theSprite == null)
                return;

            string selectedPath = AssetDatabase.GetAssetPath(theSprite);
            if (string.IsNullOrEmpty(selectedPath))
            {
                Debug.LogWarning("[ShrinkSlicedSprite] Could not determine asset path for sprite.");
                return;
            }

            // border: {left, bottom, right, top}
            int borderLeft = (int)theSprite.border.x;
            int borderRight = (int)theSprite.border.z;
            int borderTop = (int)theSprite.border.w;
            int borderBottom = (int)theSprite.border.y;

            bool shouldShortenWidth = (borderLeft > 0 && borderRight > 0);
            bool shouldShortenHeight = (borderTop > 0 && borderBottom > 0);
            bool shouldModifyTheTexture = shouldShortenWidth || shouldShortenHeight;

            if (!shouldModifyTheTexture)
                return;

            Texture2D theTexture = new Texture2D(theSprite.texture.width, theSprite.texture.height);
            theTexture.LoadImage(File.ReadAllBytes(selectedPath));

            int originalWidth = theTexture.width;
            int originalHeight = theTexture.height;

            // make the new size a multiple of 4
            int newTextureWidth = shouldShortenWidth ? (borderLeft + borderRight) : originalWidth;
            int newTextureHeight = shouldShortenHeight ? (borderTop + borderBottom) : originalHeight;
            if (shouldShortenWidth)
                newTextureWidth += (4 - newTextureWidth % 4);
            if (shouldShortenHeight)
                newTextureHeight += (4 - newTextureHeight % 4);

            if (newTextureWidth >= originalWidth && newTextureHeight >= originalHeight)
                return;

            Texture2D newTexture = new Texture2D(newTextureWidth, newTextureHeight);

            for (int w = 0; w < newTextureWidth; w++)
            {
                for (int h = 0; h < newTextureHeight; h++)
                {
                    int originW = w;
                    if (shouldShortenWidth)
                    {
                        if (w > borderLeft && w < newTextureWidth - borderRight)
                            originW = borderLeft + (originalWidth - borderLeft - borderRight) / 2;
                        else if (w >= newTextureWidth - borderRight)
                            originW = originalWidth - (newTextureWidth - w);
                    }

                    int originH = h;
                    if (shouldShortenHeight)
                    {
                        if (h > borderBottom && h < newTextureHeight - borderTop)
                            originH = borderBottom + (originalHeight - borderBottom - borderTop) / 2;
                        else if (h >= newTextureHeight - borderTop)
                            originH = originalHeight - (newTextureHeight - h);
                    }

                    newTexture.SetPixel(w, h, theTexture.GetPixel(originW, originH));
                }
            }

            newTexture.Apply();
            string backupPath = BackupOriginalFile(selectedPath);
            File.WriteAllBytes(selectedPath, newTexture.EncodeToPNG());
            AssetDatabase.ImportAsset(selectedPath);
            Debug.Log(string.Format("Sliced sprite was shrunk to w:{0} h:{1}. Backup: {2}", newTextureWidth, newTextureHeight, backupPath));
        }

        private static string BackupOriginalFile(string assetPath)
        {
            string backupPath = GetBackupPath(assetPath);
            string backupDir = Path.GetDirectoryName(backupPath);
            if (!Directory.Exists(backupDir))
                Directory.CreateDirectory(backupDir);

            File.Copy(assetPath, backupPath, true);
            return backupPath;
        }

        private static string GetBackupPath(string assetPath)
        {
            return Path.Combine(BackupRoot, assetPath);
        }

        private static bool TryGetSelectedSprite(out Sprite sprite)
        {
            sprite = null;
            if (Selection.activeObject == null)
                return false;

            sprite = Selection.activeObject as Sprite;
            if (sprite != null)
                return true;

            if (Selection.activeObject is Texture2D)
            {
                string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                var allAssets = AssetDatabase.LoadAllAssetsAtPath(selectedPath);
                foreach (var asset in allAssets)
                {
                    if (asset is Sprite subSprite)
                    {
                        sprite = subSprite;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
