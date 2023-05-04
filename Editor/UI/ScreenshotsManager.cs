using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.EditorCoroutines.Editor;
using Unity.MemoryProfiler.Editor;
using UnityEditor;

internal class ScreenshotsManager
{
    readonly struct Request
    {
        public readonly string FilePath;
        public readonly Texture2D Texture;

        public Request(string filePath, Texture2D texture)
        {
            FilePath = filePath;
            Texture = texture;
        }
    }

    Queue<Request> m_Queue;
    EditorCoroutine m_Loader;
    Dictionary<string, Texture2D> m_LoadedTextures;

    const int k_ThumbWidth = 256;
    const int k_ThumbHeight = 144;
    const string k_SnapshotScreenshotFileExtension = ".png";
    const string k_SnapshotScreenshotThumbExtension = ".bc7";
    const int k_WidthOrHeightDataSize = sizeof(int);
    const int k_ThumbWidthFileOffset = k_WidthOrHeightDataSize * 2;
    const int k_ThumbHeightFileOffset = k_WidthOrHeightDataSize;

    public event Action<string> ScreenshotLoaded;

    public ScreenshotsManager()
    {
        m_Queue = new Queue<Request>();
        m_LoadedTextures = new Dictionary<string, Texture2D>();
    }

    public static string ToScreenshotPath(string thePath)
    {
        return Path.ChangeExtension(thePath, k_SnapshotScreenshotFileExtension);
    }

    public static string ToThumbnailPath(string thePath)
    {
        return Path.ChangeExtension(thePath, k_SnapshotScreenshotThumbExtension);
    }

    public static void SnapshotDeleted(string snapshotPath)
    {
        string screenshotPath = ToScreenshotPath(snapshotPath);
        if (File.Exists(screenshotPath))
            File.Delete(screenshotPath);

        string thumbsPath = ToThumbnailPath(snapshotPath);
        if (File.Exists(thumbsPath))
            File.Delete(thumbsPath);
    }

    public static void SnapshotRenamed(string snapshotFrom, string snapshotTo)
    {
        string sourceScreenshotPath = ToScreenshotPath(snapshotFrom);
        if (File.Exists(sourceScreenshotPath))
        {
            var targetScreenshotPath = ToScreenshotPath(snapshotTo);
            File.Move(sourceScreenshotPath, targetScreenshotPath);
        }

        string thumbPathFrom = ToThumbnailPath(snapshotFrom);
        if (File.Exists(thumbPathFrom))
        {
            string thumbPathTo = ToThumbnailPath(snapshotTo);
            File.Move(thumbPathFrom, thumbPathTo);
        }
    }

    public static void SnapshotImported(string snapshotFrom, string snapshotTo)
    {
        string sourceScreenshotPath = ToScreenshotPath(snapshotFrom);
        if (File.Exists(sourceScreenshotPath))
        {
            var targetScreenshotPath = ToScreenshotPath(snapshotTo);
            File.Copy(sourceScreenshotPath, targetScreenshotPath);
        }

        string thumbPathFrom = ToThumbnailPath(snapshotFrom);
        if (File.Exists(thumbPathFrom))
        {
            string thumbPathTo = ToThumbnailPath(snapshotTo);
            File.Copy(thumbPathFrom, thumbPathTo);
        }
    }

    public Texture Enqueue(string fileName)
    {
        if (m_LoadedTextures.TryGetValue(fileName, out var texture))
            return texture;

        texture = new Texture2D(1, 1, TextureFormat.RGB24, false);
        m_LoadedTextures.Add(fileName, texture);

        var request = new Request(fileName, texture);
        m_Queue.Enqueue(request);

        if (m_Loader == null)
            m_Loader = EditorCoroutineUtility.StartCoroutine(ProcessRequest(), this);

        return texture;
    }

    public void Invalidate()
    {
        if (m_Loader != null)
            EditorCoroutineUtility.StopCoroutine(m_Loader);

        m_Queue.Clear();
        m_LoadedTextures.Clear();
    }

    IEnumerator ProcessRequest()
    {
        while (m_Queue.Count > 0)
        {
            yield return null;

            var request = m_Queue.Dequeue();

            var thumbPath = ToThumbnailPath(request.FilePath);

            if (File.Exists(thumbPath))
            {
                var data = File.ReadAllBytes(thumbPath);
                var fileSize = data.Length;

                // Read in width and height that we appended to the end of the file
                int thumbW = BitConverter.ToInt32(data, fileSize - k_ThumbWidthFileOffset);
                int thumbH = BitConverter.ToInt32(data, fileSize - k_ThumbHeightFileOffset);
                Array.Resize(ref data, fileSize - k_WidthOrHeightDataSize);

                request.Texture.Reinitialize(thumbW, thumbH, TextureFormat.BC7, false);
                request.Texture.LoadRawTextureData(data);
                request.Texture.Apply(false, true);
            }
            else
            {
                // Read in the full size screenshot so we can make a thumbnail
                var dataOriginal = File.ReadAllBytes(request.FilePath);
                Texture2D fullSizeTex = new Texture2D(1, 1, TextureFormat.RGB24, false);
                fullSizeTex.LoadImage(dataOriginal, false);
                fullSizeTex.filterMode = FilterMode.Bilinear;

                // Make a rendertexture with the same aspect ratio, scaled to fit within thumbnail bounds:
                // First find out what ratio we're dealing with.
                float screenshotAspect = (float)fullSizeTex.width / (float)fullSizeTex.height;
                int thumbWidth = k_ThumbWidth;
                int thumbHeight = k_ThumbHeight;

                // Find our final shrunk width and height.
                // Round the numbers so that our final dimensions are multiples of 4 for BC7 compression.
                if (screenshotAspect > 16.0f / 9.0f)
                    thumbHeight = 4 * (int)((float)thumbWidth / (screenshotAspect * 4));
                else
                    thumbWidth = 4 * (int)((float)(thumbHeight / 4) * screenshotAspect);

                // Get a temp RT with the final dimensions and send our original image to it
                var tempRt = RenderTexture.GetTemporary(thumbWidth, thumbHeight);
                tempRt.filterMode = FilterMode.Bilinear;
                RenderTexture.active = tempRt;
                Graphics.Blit(fullSizeTex, tempRt);

                // Read the final texture back from the RT and compress to BC7.
                request.Texture.Reinitialize(thumbWidth, thumbHeight);
                request.Texture.ReadPixels(new Rect(0, 0, thumbWidth, thumbHeight), 0, 0);
                EditorUtility.CompressTexture(request.Texture, TextureFormat.BC7, TextureCompressionQuality.Fast);

                // Write out the compressed raw data to file + the dimensions.
                using (var fStream = new FileStream(thumbPath, FileMode.Append))
                {
                    var rawData = request.Texture.GetRawTextureData();
                    fStream.Write(rawData, 0, rawData.Length);
                    fStream.Write(BitConverter.GetBytes(thumbWidth), 0, k_WidthOrHeightDataSize);
                    fStream.Write(BitConverter.GetBytes(thumbHeight), 0, k_WidthOrHeightDataSize);
                }
                File.SetAttributes(thumbPath, File.GetAttributes(thumbPath) | FileAttributes.Hidden);

                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(tempRt);
                UnityEngine.Object.DestroyImmediate(fullSizeTex);
            }

            request.Texture.name = request.FilePath;
            ScreenshotLoaded?.Invoke(request.FilePath);
        }

        EditorCoroutineUtility.StopCoroutine(m_Loader);
        m_Loader = null;
    }
}
