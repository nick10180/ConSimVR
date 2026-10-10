using UnityEngine;
using UnityEngine.UI;

namespace ConSimVR
{
    /// <summary>
    /// The OS mouse cursor is drawn by Windows on top of the window, so it never appears in the captured UI. This draws
    /// an arrow on a top-most screen-space canvas that follows the mouse while the game shows its cursor, which puts it
    /// into the capture shown on the VR UI panel. It is not a raycast target, so clicks pass through to the game.
    /// </summary>
    internal sealed class UiCursor
    {
        private const int TextureSize = 32;

        private GameObject _root;
        private RectTransform _arrow;
        private bool _shown;

        public void Create()
        {
            if (_root != null)
                return;
            _root = new GameObject("ConSimVR Cursor") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var arrow = new GameObject("Arrow") { hideFlags = HideFlags.HideAndDontSave };
            arrow.transform.SetParent(_root.transform, false);
            var image = arrow.AddComponent<Image>();
            image.raycastTarget = false;
            image.sprite = Sprite.Create(CreateArrowTexture(), new Rect(0, 0, TextureSize, TextureSize), new Vector2(0f, 1f), 100f);
            _arrow = arrow.GetComponent<RectTransform>();
            _arrow.pivot = new Vector2(0f, 1f);
            _arrow.anchorMin = _arrow.anchorMax = Vector2.zero;
            _root.SetActive(false);
        }

        /// <summary>Main thread, LateUpdate: follows the mouse while the game's cursor is visible.</summary>
        public void Update(bool panelVisible)
        {
            if (_root == null)
                return;
            bool show = panelVisible && Cursor.visible && Cursor.lockState != CursorLockMode.Locked;
            if (show != _shown)
            {
                _root.SetActive(show);
                _shown = show;
            }
            if (!show)
                return;
            float size = TextureSize * Mathf.Max(1f, Screen.height / 1080f);
            _arrow.sizeDelta = new Vector2(size, size);
            var mouse = Input.mousePosition;
            _arrow.position = new Vector3(mouse.x, mouse.y, 0f);
        }

        public void Destroy()
        {
            if (_root != null)
                Object.Destroy(_root);
            _root = null;
            _arrow = null;
            _shown = false;
        }

        /// <summary>A classic white arrow with a black outline, tip at the top-left corner.</summary>
        private static Texture2D CreateArrowTexture()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    // Arrow coordinates: u right, v down from the tip.
                    int u = x, v = TextureSize - 1 - y;
                    bool inside = InArrow(u, v);
                    bool edge = inside && (!InArrow(u - 1, v) || !InArrow(u + 1, v) || !InArrow(u, v - 1) || !InArrow(u, v + 1));
                    texture.SetPixel(x, y, !inside ? clear : edge ? Color.black : Color.white);
                }
            }
            texture.Apply(false, true);
            return texture;
        }

        private static bool InArrow(int u, int v)
        {
            if (u < 0 || v < 0 || v >= 26)
                return false;
            if (v < 19)
                return u <= v * 0.62f;           // the head: a wedge widening downwards
            if (u <= 4 && v < 25)
                return true;                      // the stem on the left
            return u >= (v - 19) * 0.55f + 4 && u <= (v - 19) * 0.55f + 9 && v < 26 && u <= 12; // the tail
        }
    }
}
