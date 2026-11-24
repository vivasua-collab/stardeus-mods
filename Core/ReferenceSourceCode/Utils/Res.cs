#if !UNITY_EDITOR
#define COMPRESS_TEX
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Platform;
using KL.Utils;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Networking;
using UnityEngine.Video;

namespace Game.Utils {
    public static partial class Res {
        private const int MagicHeader = 0x1BADCAFE;
        internal const int RawTextureMagic = MagicHeader;
        static Res() {
            compressTextures = true;
            if (PlatformUtils.HasCommandLineArg("--uncompressed-textures")) {
                D.Warn("--uncompressed-textures detected, won't do texture compression");
                compressTextures = false;
            }
            #if UNITY_EDITOR
            // D.Warn("Skipping texture compression in Unity editor");
            // compressTextures = false;
            #endif
        }

        public static bool WantsCompressedTextures;
        private static bool compressTextures = true;
        // Whether textures end up compressed on the GPU (--uncompressed-textures turns it off)
        public static bool CompressesTextures => WantsCompressedTextures && compressTextures;

        #if PRODUCTION_BUILD
        public const string TextureSizePref = "tex.quality";
        #else
        public static string TextureSizePref {
            get {
                if (Coroutines.IsRunningTests) {
                    return "tex.quality.test";
                }
                return "tex.quality.dev";
            }
        }
        #endif
        private static readonly Dictionary<string, GameObject> cache = new();
        public static GameObject LoadCached(string prefabPath) {
            if (cache.TryGetValue(prefabPath, out GameObject result)) {
                return result;
            }
            try {
                var res = Resources.Load(prefabPath) as GameObject;
                cache.Add(prefabPath, res);
                // D.Log("Res.LoadCached caching new GameObject: {0}", prefabPath);
                return res;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed loading prefab: {0}", prefabPath);
                return null;
            }
        }

        public static string[] LoadTextLines(string path) {
            D.Ass(File.Exists(path), "Streaming file does not exist! {0}", path);
            return File.ReadAllLines(path);
        }

        public static string[] ListFiles(string path) {
            D.Ass(Directory.Exists(path), "Dir does not exist! {0}", path);
            var plen = path.Length;
            // OSX can create "._File.json"
            return Directory.EnumerateFiles(path)
                .Where(f => !f.StartsWith('.'))
                .Select(f => f.Remove(0, plen + 1))
            .ToArray();
        }

        /// <summary>
        /// Includes .files
        /// </summary>
        public static string[] ListAllFilesRecursive(string path, bool fullPath) {
            if (path.EndsWith("/")) {
                D.Err("Path should not end with trailing slash!");
            }
            D.Ass(Directory.Exists(path), "Streaming dir does not exist! {0}", path);
            var res = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);
            if (!fullPath) {
                var plen = path.Length;
                res = res.Select(f => f[(plen + 1)..].Replace(@"\", "/"));
            }
            return res.ToArray();
        }

        public static string[] ListFilesRecursive(string path, params string[] extensions) {
            if (path.EndsWith("/")) {
                D.Err("Path should not end with trailing slash! {0}", path);
                path = path.TrimEnd('/');
            }
            D.Ass(Directory.Exists(path), "Streaming dir does not exist! {0}", path);
            var plen = path.Length;
            var slashDot = $"{Path.DirectorySeparatorChar}.";
            if (extensions?.Length > 0) {
                return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => !f.StartsWith('.') && !f.Replace(path, string.Empty).Contains(slashDot)
                        && extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(f => f.Remove(0, plen + 1)).ToArray();
            } else {
                return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => !f.StartsWith('.') && !f.Replace(path, string.Empty).Contains(slashDot))
                    .Select(f => f.Remove(0, plen + 1)).ToArray();
            }
        }
        public static IEnumerator LoadAudioClipAsync(string fullPath,
                Action<AudioClip> onLoad) {
            string url = ToUrl(fullPath);
            UnityWebRequest req = null;
            try {
                req = UnityWebRequestMultimedia.GetAudioClip(url,
                    ResolveAudioType(url));
                req.SendWebRequest();
            } catch (Exception ex) {
                D.LogEx(ex, "Failed loading audio: {0}", url);
                yield break;
            }
            while (!req.isDone) { yield return null; }
            if (req.result != UnityWebRequest.Result.Success) {
                D.Err("Failed getting url: {0}: {1}", url, req.error);
                yield break;
            }
            while (!req.downloadHandler.isDone) { yield return null; }
            var clip = ((DownloadHandlerAudioClip) req.downloadHandler).audioClip;
            onLoad(clip);
        }

        private static AudioType ResolveAudioType(string url) {
            if (url.EndsWith(".ogg")) { return AudioType.OGGVORBIS; }
            if (url.EndsWith(".mp3")) { return AudioType.MPEG; }
            if (url.EndsWith(".wav")) { return AudioType.WAV; }
            return AudioType.UNKNOWN;
        }

        public static Sprite LoadSprite(string fullPath) {
            return Resources.Load<Sprite>(fullPath);
        }
        public static VideoClip LoadVideoClip(string fullPath) {
            var clip = Resources.Load<VideoClip>(fullPath);
            if (clip == null) {
                D.Err("VideoClip not found in resources: {0}", fullPath);
            }
            return clip;
        }
        // TODO experiment with this
        public const int basePixelsPerUnit = 64;
        public static int PixelsPerUnit = 64;
        public static Sprite CreateSpriteFrom(Texture2D tex) {
            if (tex == null) { return null; }
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), PixelsPerUnit, 0,
                    SpriteMeshType.FullRect, Vector4.zero, false);
        }
        #if false
        public static IEnumerator LoadSpriteAsync(string fullPath,
                Action<Sprite> onLoad, bool warn = true, bool isLocalFile = true) {
            yield return LoadTextureAsync(fullPath, (tex) => {
                onLoad(Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), PixelsPerUnit, 0,
                    SpriteMeshType.FullRect, Vector4.zero, false));
            }, warn, isLocalFile);
        }
        #endif

        public static void Finalize(Texture2D tex) {
            // A texture loaded from the graphics cache can already be compressed
            if (CompressesTextures && !GraphicsFormatUtility.IsCompressedFormat(tex.format)) {
                try {
                    using (BootTimeline.Span("Texture compress"))
                    using (D.Timer($"Compress texture: {tex.name}")) {
                        tex.Compress(true);
                    }
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failed to compress texture: {0}", tex.name);
                }
            }
            try {
                using (BootTimeline.Span("Texture upload")) {
                    tex.Apply(false, true);
                }
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed to apply texture: {0}", tex.name);
            }
        }

        public static void ScaleTex(Texture2D tex, int width, int height,
                FilterMode mode = FilterMode.Bilinear, bool makeNonReadable = false) {
            Rect texR = new Rect(0, 0, width, height);
            var tempTex = GPUScale(tex, width, height, mode);
            tex.Reinitialize(width, height);
            //tex.Reinitialize(width, height);
            tex.ReadPixels(texR, 0, 0, true);

            Graphics.SetRenderTarget(null);
            tempTex.Release();
            The.Alloc.Unregister(tempTex);
            GameObject.DestroyImmediate(tempTex);

            tex.Apply(true, makeNonReadable);
        }

        public static RenderTexture GPUScale(Texture2D src, int width, int height, FilterMode fmode) {
            //We need the source texture in VRAM because we render with it
            src.filterMode = fmode;
            src.Apply(true);

            var rtt = new RenderTexture(width, height, 32);
            The.Alloc.Register(rtt);
            Graphics.SetRenderTarget(rtt);

            //Setup 2D matrix in range 0..1, so nobody needs to care about sized
            GL.LoadPixelMatrix(0, 1, 1, 0);

            //Then clear & draw the texture to fill the entire RTT.
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            Graphics.DrawTexture(new Rect(0, 0, 1, 1), src);


            // Need to call this afterwards in the caller:
            // Graphics.SetRenderTarget(null);
            // rtt.Release();
            // GameObject.DestroyImmediate(rtt);
            return rtt;
        }

        public static Texture2D LoadTexture(string fullPath, bool mips, bool makeNonReadable) {
            return LoadTexturePNGOptimized(fullPath, mips, makeNonReadable);
            // var bytes = File.ReadAllBytes(fullPath);
            // var tex = new Texture2D(2, 2, TextureFormat.ARGB32, mips, false);
            // tex.name = Path.GetFileName(fullPath);
            // tex.LoadImage(bytes, makeNonReadable);
            // The.Alloc.Register(tex);
            // return tex;
        }

        /// <summary>
        /// Exports the texture with size, format, mipmaps and compression from GPU memory.
        /// </summary>
        /// <param name="tex"></param>
        /// <returns></returns>
        public static byte[] GetTextureRawData(Texture2D tex) {
            if (tex == null) {
                return null;
            }

            try {
                // Get raw texture data from GPU
                var rawData = tex.GetRawTextureData();

                // Create header with texture metadata (28 bytes total)
                var header = new byte[28];
                var offset = 0;

                // Write magic number for validation (4 bytes)
                System.BitConverter.GetBytes(MagicHeader).CopyTo(header, offset); // "TEXT"
                offset += 4;

                // Write dimensions (8 bytes)
                System.BitConverter.GetBytes(tex.width).CopyTo(header, offset);
                offset += 4;
                System.BitConverter.GetBytes(tex.height).CopyTo(header, offset);
                offset += 4;

                // Write format (4 bytes)
                System.BitConverter.GetBytes((int)tex.format).CopyTo(header, offset);
                offset += 4;

                // Write mipmap count (4 bytes)
                System.BitConverter.GetBytes(tex.mipmapCount).CopyTo(header, offset);
                offset += 4;

                // Write mipmap bias (4 bytes)
                System.BitConverter.GetBytes(tex.mipMapBias).CopyTo(header, offset);

                // Combine header and raw data
                var result = new byte[header.Length + rawData.Length];
                System.Array.Copy(header, 0, result, 0, header.Length);
                System.Array.Copy(rawData, 0, result, header.Length, rawData.Length);

                return result;
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed to get raw texture data from: {0}", tex.name);
                return null;
            }
        }

        // Writes what GetTextureRawData returns, but streams the texture's own memory into the
        // file: GetTextureRawData makes two managed copies, 2 x 341 MiB for an 8192 atlas page
        public static bool WriteTextureRawData(Texture2D tex, string path) {
            return WriteTextureRawData(path, tex.width, tex.height, tex.format, tex.mipmapCount,
                tex.mipMapBias, tex.GetRawTextureData<byte>());
        }

        // Any thread: the atlas build writes a page's pixels while the main thread goes on
        public static bool WriteTextureRawData(string path, int width, int height,
                TextureFormat format, int mipCount, float mipMapBias, NativeArray<byte> pixels) {
            try {
                var header = new byte[28];
                System.BitConverter.GetBytes(MagicHeader).CopyTo(header, 0);
                System.BitConverter.GetBytes(width).CopyTo(header, 4);
                System.BitConverter.GetBytes(height).CopyTo(header, 8);
                System.BitConverter.GetBytes((int) format).CopyTo(header, 12);
                System.BitConverter.GetBytes(mipCount).CopyTo(header, 16);
                System.BitConverter.GetBytes(mipMapBias).CopyTo(header, 20);
                using var file = new FileStream(path, FileMode.Create, FileAccess.Write,
                    FileShare.None, 1 << 20);
                file.Write(header, 0, header.Length);
                unsafe {
                    var ptr = (byte*) NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(pixels);
                    using var src = new UnmanagedMemoryStream(ptr, pixels.Length);
                    src.CopyTo(file, 1 << 20);
                }
                return true;
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed writing raw texture data: {0}", path);
                return false;
            }
        }

        /// <summary>
        /// Recreates the Texture2D from path to file with raw bytes exported using
        /// <see cref="GetTextureRawData(Texture2D)"/>
        /// </summary>
        /// <param name="fullPath"></param>
        /// <param name="finalize">Whether to compress and finalize the texture after loading</param>
        /// <returns></returns>
        [Obsolete("Use LoadTextureRawDataOptimized/Async instead. This method is deprecated due to poor performance.", error: false)]
        public static Texture2D LoadTextureRawData(string fullPath, bool finalize = true) {
            if (!File.Exists(fullPath)) {
                return null;
            }

            try {
                var fileData = File.ReadAllBytes(fullPath);

                if (fileData.Length < 28) {
                    D.Err("Raw texture file too small: {0}", fullPath);
                    return null;
                }

                var offset = 0;

                // Validate magic number
                var magic = System.BitConverter.ToInt32(fileData, offset);
                offset += 4;
                if (magic != MagicHeader) {
                    D.Err("Invalid raw texture file format: {0}", fullPath);
                    return null;
                }

                // Read metadata
                var width = System.BitConverter.ToInt32(fileData, offset);
                offset += 4;
                var height = System.BitConverter.ToInt32(fileData, offset);
                offset += 4;
                var format = (TextureFormat)System.BitConverter.ToInt32(fileData, offset);
                offset += 4;
                var mipmapCount = System.BitConverter.ToInt32(fileData, offset);
                offset += 4;
                var mipMapBias = System.BitConverter.ToSingle(fileData, offset);
                offset += 4;

                // Create texture with original properties
                var tex = new Texture2D(width, height, format, mipmapCount > 1, false);
                // tex.name = Path.GetFileNameWithoutExtension(fullPath);
                tex.name = Path.GetFileName(fullPath);

                // Load raw data
                var rawDataSize = fileData.Length - 28;
                var rawData = new byte[rawDataSize];
                System.Array.Copy(fileData, 28, rawData, 0, rawDataSize);

                tex.LoadRawTextureData(rawData);
                tex.mipMapBias = mipMapBias;
                tex.Apply(false, false);

                // Optionally recompress the texture to match original compression state
                if (finalize) {
                    Finalize(tex);
                }

                The.Alloc.Register(tex);
                return tex;
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed to load raw texture data from: {0}", fullPath);
                return null;
            }
        }

        public static Texture2D LoadTexture(string fullPath, bool mips, bool makeNonReadable, bool skipRescale) {
            var tex = LoadTexturePNGOptimized(fullPath, mips, skipRescale && makeNonReadable);
            if (skipRescale) { return tex; }
            ScaleToTextureQuality(tex);
            return tex;
        }

        // Whether sprites are scaled down for the texture quality setting, on the GPU
        public static bool RescalesSprites => CachedPlayerPrefs.GetInt(TextureSizePref, 0) > 0;

        // A sprite PngDecoder decoded on a worker (RGBA), as LoadTexture would have loaded it.
        // Not uploaded: the atlas build reads its pixels, and scaling uploads it itself.
        public static Texture2D SpriteTexture(string fullPath, int width, int height,
                NativeArray<byte> rgba, bool skipRescale) {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            tex.name = Path.GetFileName(fullPath);
            tex.SetPixelData(rgba, 0);
            The.Alloc.Register(tex);
            if (!skipRescale) {
                ScaleToTextureQuality(tex);
            }
            return tex;
        }

        private static void ScaleToTextureQuality(Texture2D tex) {
            var scale = CachedPlayerPrefs.GetInt(TextureSizePref, 0);
            if (scale > 0) {
                var w = tex.width;
                var h = tex.height;
                if (Maths.IsPOT(w) && Maths.IsPOT(h)) {
                    for (int i = 0; i < scale; i++) {
                        if (Maths.IsPOT(w / 2) && Maths.IsPOT(h / 2)) {
                            w /= 2;
                            h /= 2;
                        } else {
                            break;
                        }
                    }
                } else {
                    // At least 1 pixel: a 0 pixel render target throws, which stopped mod atlas
                    // packing and hung the boot (a mod's 1x1 sprite at reduced texture quality)
                    w = Mathf.Max(1, tex.width / (int)(Mathf.Pow(2, scale)));
                    h = Mathf.Max(1, tex.height / (int)(Mathf.Pow(2, scale)));
                }
                ScaleTex(tex, w, h);
            }
        }

        public static Texture2D AddMipMaps(Texture2D input, float bias) {
            if (input.mipmapCount > 1) {
                if (input.mipMapBias != bias) {
                    input.mipMapBias = bias;
                }
                return input;
            }
            D.Ass(input.isReadable, "Input texture is not readable!");
            var newTex = new Texture2D(input.width, input.height, input.format, true);
            The.Alloc.Register(newTex);
            newTex.name = input.name;
            Graphics.CopyTexture(input, 0, 0, newTex, 0, 0);
            newTex.filterMode = input.filterMode;
            newTex.wrapMode = input.wrapMode;
            newTex.mipMapBias = bias;
            The.Alloc.UnregisterAndDispose(input);
            newTex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            // newTex.Apply(true);
            return newTex;
        }
        public static Texture2D RemoveMipMaps(Texture2D input) {
            if (input.mipmapCount < 2) {
                //D.Err("Texture already has no mipmaps: {0}", input.name);
                return input;
            }
            D.Ass(input.isReadable, "Input texture is not readable!");
            var newTex = new Texture2D(input.width, input.height, input.format, false);
            The.Alloc.Register(newTex);
            newTex.name = input.name;
            Graphics.CopyTexture(input, 0, 0, newTex, 0, 0);
            newTex.filterMode = input.filterMode;
            newTex.wrapMode = input.wrapMode;
            The.Alloc.UnregisterAndDispose(input);
            // newTex.Apply(true);
            return newTex;
        }

        private static string ToUrl(string url) {
            // Cannot do that!
            // url = UnityWebRequest.EscapeURL(url);
            url = url.Replace("+", "%2B");
            if (url.StartsWith("file:///") || url.StartsWith("http")) {
                return url;
            }
            if (url.StartsWith("file://")) {
                return url.Replace("file://", "file:///");
            }
            if (url.StartsWith("/")) {
                return "file://" + url;
            }
            return "file:///" + url;
        }

        public static string LoadText(string path) {
            var r = Resources.Load<TextAsset>(path);
            if (r != null) {
                return r.text.Trim();
            }
            return null;
        }

        public static IEnumerator LoadUrlAsText(string url,
                Action<string> callback,
                Action<UnityWebRequest> failCallback = null) {
            url = ToUrl(url);
            //Debug.LogFormat("Downloading text: {0}", url);
            var req = UnityWebRequest.Get(url);
            req.timeout = 5;
            req.redirectLimit = 2;
            yield return req.SendWebRequest();
            int iterator = 0;
            while (!req.isDone && req.result == UnityWebRequest.Result.Success) {
                if (iterator++ > 10000) {
                    D.Err("Could not download url for 10k yields: {0}", url);
                    failCallback(req);
                    yield break;
                }
                yield return null;
            }
            if (req.result != UnityWebRequest.Result.Success) {
                // D.Warn("Failed loading text url: {0}. Error: {1}", url, req.error);
                failCallback?.Invoke(req);
                yield break;
            }
            //// D.Log("Downloaded text successfully, now calling callback for url: {0}", url);
            callback(req.downloadHandler.text);
        }
    }
}
