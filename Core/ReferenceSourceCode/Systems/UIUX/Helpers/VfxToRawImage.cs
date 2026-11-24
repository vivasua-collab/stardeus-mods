using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VFX;

public sealed class VfxToRawImage : MonoBehaviour {
    // Each holder gets its own camera filming its own VFX on a private Z slab, isolated from
    // every other holder's slab by assigning slots from a shared counter instead of a random
    // offset. A random offset in a wide-but-finite range collides often enough in practice (two
    // holders within one slab width of each other) that one holder's camera would also pick up
    // the other's VFX (both are on the TransparentFX layer), rendering it oversized/bleeding past
    // the edges of the render texture until the colliding holders happened to be destroyed and
    // re-rolled (e.g. by a save/load rebuilding the whole starmap).
    private const float slotStride = 200f;
    private static readonly Stack<int> freeSlots = new();
    private static int nextSlotIndex;
    private int slotIndex = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSlots() {
        freeSlots.Clear();
        nextSlotIndex = 0;
    }

    private RawImage rawImage;
    private int vfxLayer;
    private Color clearColor = Color.clear;
    private float orthographicSize = 1f;
    private float cameraDistance = 0.5f;
    private float nearClipPlane = 0.01f;
    private float farClipPlane = 0.99f;
    private float zOffset;

    private VisualEffect visualEffect;
    private Camera cam;
    private GameObject cameraGO;
    private RenderTexture rt;
    private RectTransform rectTransform;
    private Vector2Int lastSize;
    private float lastFrustumScale;
    private bool isInitialized;
    private Material material;

    public void Initialize(RawImage rawImage, int vfxLayer, float orthographicSize = 100f,
            float cameraDistance = 0f, float nearClip = -50f, float farClip = 50f) {
        this.rawImage = rawImage;
        this.vfxLayer = vfxLayer;
        this.orthographicSize = orthographicSize;
        this.cameraDistance = cameraDistance;
        this.nearClipPlane = nearClip;
        this.farClipPlane = farClip;
        rectTransform = (RectTransform) transform;
        slotIndex = freeSlots.Count > 0 ? freeSlots.Pop() : nextSlotIndex++;
        zOffset = slotIndex * slotStride;

        CreateOrResizeRenderTexture();
        CreateCamera();
        CreateMaterial();
        if (this.rawImage != null) {
            this.rawImage.texture = rt;
            this.rawImage.material = material;
        }
        isInitialized = true;
    }

    public void SetVisualEffect(VisualEffect vfx) {
        visualEffect = vfx;
        AssignLayer();
        PositionVFX();
    }

    private void LateUpdate() {
        if (!isInitialized) {
            return;
        }
        CreateOrResizeRenderTexture();
        UpdateCameraFrustum();
    }

    // A Camera's view matrix is built from position and rotation only - transform scale is
    // ignored - but the VisualEffect we film is not scale invariant: its particles are sized in
    // world units, so they follow the UI hierarchy's lossyScale. With a fixed world-space
    // frustum the effect would fill a smaller slice of the render texture the further the
    // starmap zooms out, and the texture quad already shrinks with zoom on its own, making the
    // effect scale with zoom squared. Scaling the frustum by lossyScale keeps it constant in the
    // holder's local space, so the effect keeps its proportions at every zoom level.
    // Objects with IsConstantSizeInStarmap stay at lossyScale 1 and are unaffected.
    private void UpdateCameraFrustum() {
        if (cam == null) {
            return;
        }
        var scale = transform.lossyScale.y;
        if (scale <= 0f || Mathf.Abs(lastFrustumScale - scale) < 0.0001f) {
            return;
        }
        lastFrustumScale = scale;
        cam.orthographicSize = orthographicSize * scale;
        // The Z slot is a localPosition, so the world gap between two holders' slots is
        // slotStride * lossyScale. Clip planes are world-space distances, so they have to scale
        // with it or a zoomed out map would pull neighboring slots inside the slab.
        cam.nearClipPlane = nearClipPlane * scale;
        cam.farClipPlane = farClipPlane * scale;
    }

    private void OnDestroy() {
        Cleanup();
    }

    private void CreateOrResizeRenderTexture() {
        if (rectTransform == null) {
            return;
        }

        Vector2 size = rectTransform.rect.size;
        int w = Mathf.Max(1, Mathf.RoundToInt(size.x));
        int h = Mathf.Max(1, Mathf.RoundToInt(size.y));

        if (rt != null && lastSize.x == w && lastSize.y == h) {
            return;
        }

        CleanupRenderTexture();

        rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32);
        rt.name = "VFX_UI_RenderTexture";
        rt.useMipMap = false;
        rt.autoGenerateMips = false;
        rt.Create();

        lastSize = new Vector2Int(w, h);

        if (cam != null) {
            cam.targetTexture = rt;
        }

        if (rawImage != null) {
            rawImage.texture = rt;
        }
    }

    private void CreateCamera() {
        if (cam != null) {
            return;
        }

        cameraGO = new GameObject("VFX_UI_Camera");
        cameraGO.transform.SetParent(transform, false);
        cameraGO.transform.localPosition = new Vector3(0f, 0f, zOffset);
        cameraGO.transform.localRotation = Quaternion.identity;

        cam = cameraGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = clearColor;
        cam.orthographic = true;
        cam.cullingMask = 1 << vfxLayer;
        cam.targetTexture = rt;
        cam.depth = -1000;
        cam.enabled = true;
        UpdateCameraFrustum();
    }

    private void PositionVFX() {
        if (visualEffect == null) {
            return;
        }
        visualEffect.transform.localPosition = new Vector3(0f, 0f, zOffset + cameraDistance);
    }

    private void AssignLayer() {
        if (visualEffect == null) {
            return;
        }
        visualEffect.gameObject.layer = vfxLayer;
    }

    private void CreateMaterial() {
        var shader = Shader.Find("UI/VFX");
        if (shader == null) {
            Debug.LogError("VfxToRawImage: Could not find shader 'UI/VFX'");
            return;
        }
        material = new Material(shader);
    }

    private void Cleanup() {
        isInitialized = false;
        CleanupCamera();
        CleanupRenderTexture();
        CleanupMaterial();
        if (slotIndex >= 0) {
            freeSlots.Push(slotIndex);
            slotIndex = -1;
        }
    }

    private void CleanupMaterial() {
        if (rawImage != null) {
            rawImage.material = null;
        }
        if (material != null) {
            Destroy(material);
            material = null;
        }
    }

    private void CleanupCamera() {
        if (cam != null) {
            cam.targetTexture = null;
        }
        if (cameraGO != null) {
            Destroy(cameraGO);
        }
        cam = null;
        cameraGO = null;
    }

    private void CleanupRenderTexture() {
        if (rawImage != null) {
            rawImage.texture = null;
        }
        if (rt != null) {
            if (rt.IsCreated()) {
                rt.Release();
            }
            Destroy(rt);
            rt = null;
        }
    }
}
