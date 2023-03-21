using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.EditorCoroutines.Editor;

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

    public event Action<string> ScreenshotLoaded;

    public ScreenshotsManager()
    {
        m_Queue = new Queue<Request>();
        m_LoadedTextures = new Dictionary<string, Texture2D>();
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
            var data = File.ReadAllBytes(request.FilePath);
            request.Texture.LoadImage(data, false);

            // TODO: here or better still on receiving the screenshot, make sure the dimensions aren't total overkill and if they are, downscale, and save the smaller resolution
            if (request.Texture.width % 4 == 0 && request.Texture.height % 4 == 0)
                request.Texture.Compress(false);
            request.Texture.Apply(false, true);
            request.Texture.name = request.FilePath;

            ScreenshotLoaded?.Invoke(request.FilePath);
        }

        EditorCoroutineUtility.StopCoroutine(m_Loader);
        m_Loader = null;
    }
}
