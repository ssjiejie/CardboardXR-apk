using System;
using System.Collections.Generic;
using CardboardXR.Stereo;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Interaction
{
    // The picture-size row on the app's settings panel: - 100% +, setting how big the passthrough
    // camera image is drawn in each eye. 100% is the fill the image has always had; 60% shrinks it,
    // showing the black around it; 200% enlarges it and crops the edges further.
    //   The row is built at runtime, so Core.unity stays untouched. The panel grows one row taller
    // (100 -> 180 dp) and its middle therefore moves down: 2.7 degrees at the panel's distance. The
    // toolbar's items move up inside it by 38 dp, 3.0 degrees, so that row stays where it was on
    // screen. The X moves from below the panel to the right end of the new row, inside the surface,
    // rather than 6 degrees lower at the lens's blurred edge.
    //   The row is a plain RectTransform under the panel, not a canvas of its own: a nested M3Canvas
    // would multiply the panel's dp scale by itself (0.0017 squared is 3e-6), shrinking 320 dp to a
    // millimetre. The panel's Canvas draws it, and its GazeCanvas raycasts it through the graphic
    // registry, which picks up graphics added at runtime.
    public class PassthroughZoomControl : MonoBehaviour
    {
        // 14 + 76 (the items) + 20 + 56 (the row) + 14 = 180 dp.
        const float k_PanelHeight = 180f;
        // The 14 dp top margin plus half an item.
        const float k_ItemY = 38f;
        // The row's middle, and the X's at its right end: half the row, and 8 dp from the edge.
        const float k_RowY = -48f;
        const float k_CloseX = 222f;
        // The X's right edge is 250 dp from the middle; narrower than this and it hangs off.
        const float k_MinPanelWidth = 500f;
        const float k_RowWidth = 320f;
        const float k_Step = 0.1f;
        // What the row looks like while passthrough is off: there's no image to size.
        const float k_DisabledAlpha = 0.38f;

        PassthroughController m_Controller;
        CanvasGroup m_Row;
        TMP_Text m_Value;
        bool m_On;
        bool m_Applied;
        float m_Shown = float.NaN;

        // Runs once, after the scene's own Awake: SettingsSummoner has hidden the panel by then, so
        // nothing here is visible while it's built.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var controller = FindFirstObjectByType<PassthroughController>();
            var toolbar = FindFirstObjectByType<M3Toolbar>(FindObjectsInactive.Include);
            if (controller == null || toolbar == null)
                return;
            var panel = (RectTransform)toolbar.transform;

            // Direct children only. A recursive Find could reach a demo's own Close button, and this
            // one is the panel's: the child the X hangs on.
            var close = FindDirect(panel, "Close");
            if (close == null)
            {
                Debug.LogWarning("[Zoom] the settings panel has no Close child; left the panel as it was.");
                return;
            }
            if (panel.sizeDelta.x < k_MinPanelWidth)
            {
                Debug.LogWarning($"[Zoom] the settings panel is only {panel.sizeDelta.x:F0} dp wide, too narrow for the row; left it as it was.");
                return;
            }

            // Everything is checked above; from here the panel is changed, and put back whole if any
            // step throws, so a half-built row can't be left on it.
            var height = panel.sizeDelta.y;
            var itemY = toolbar.ItemY;
            var closePosition = close.anchoredPosition;
            var built = new List<GameObject>();
            PassthroughZoomControl control = null;
            try
            {
                panel.sizeDelta = new Vector2(panel.sizeDelta.x, k_PanelHeight);
                toolbar.ItemY = k_ItemY;
                close.anchoredPosition = new Vector2(k_CloseX, k_RowY);
                control = Build(panel, controller, built);
            }
            catch (Exception e)
            {
                if (control != null)
                {
                    control.enabled = false;
                    Destroy(control);
                }
                foreach (var go in built)
                    if (go != null)
                        Destroy(go);
                panel.sizeDelta = new Vector2(panel.sizeDelta.x, height);
                toolbar.ItemY = itemY;
                close.anchoredPosition = closePosition;
                Debug.LogWarning($"[Zoom] could not build the row ({e.GetType().Name}: {e.Message}); the panel is back as it was.");
                return;
            }

            Debug.Log("[Zoom] the settings panel has the picture-size row: 60% to 200%, 100% is the fill.");
        }

        static PassthroughZoomControl Build(RectTransform panel, PassthroughController controller, List<GameObject> built)
        {
            var control = panel.gameObject.AddComponent<PassthroughZoomControl>();
            control.m_Controller = controller;

            var row = M3.Group("Picture size", panel, Vector2.zero, new Vector2(k_RowWidth, M3Theme.k_Target));
            built.Add(row.gameObject);
            Center(row, new Vector2(0f, k_RowY));
            control.m_Row = row.gameObject.AddComponent<CanvasGroup>();

            var down = M3.Button("Picture size down", row, "-", M3Button.Variant.Tonal, new Vector2(-110f, 0f),
                () => control.Step(-1), null, 72f);
            built.Add(down.gameObject);
            Center((RectTransform)down.transform, new Vector2(-110f, 0f));

            var up = M3.Button("Picture size up", row, "+", M3Button.Variant.Tonal, new Vector2(110f, 0f),
                () => control.Step(1), null, 72f);
            built.Add(up.gameObject);
            Center((RectTransform)up.transform, new Vector2(110f, 0f));

            var value = M3.Text("Picture size value", row, "100%", M3Theme.Type.LabelLarge, Vector2.zero,
                new Vector2(140f, M3Theme.k_Target), M3Theme.OnSurfaceVariant);
            built.Add(value.gameObject);
            Center(value.rectTransform, Vector2.zero);
            // A read-out, not a target: aiming at the number shouldn't stop a pinch reaching through.
            value.raycastTarget = false;
            control.m_Value = value;

            return control;
        }

        // Anchors an element at its parent's middle, which is where all the positions here are from.
        static void Center(RectTransform rect, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
        }

        // A direct child with this name, ignoring case.
        static RectTransform FindDirect(RectTransform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                    return child as RectTransform;
            }
            return null;
        }

        void OnEnable() => Apply();

        void Step(int direction)
        {
            if (m_Controller != null)
                m_Controller.Scale += direction * k_Step;
        }

        void LateUpdate()
        {
            if (m_Controller == null)
                return;
            if (!m_Applied || m_Controller.IsOn != m_On)
                Apply();
            var scale = m_Controller.Scale;
            if (!Mathf.Approximately(scale, m_Shown))
            {
                m_Shown = scale;
                if (m_Value != null)
                    m_Value.text = $"{Mathf.RoundToInt(scale * 100f)}%";
            }
        }

        // Dims the row and stops it taking taps while there's no image to size.
        void Apply()
        {
            if (m_Controller == null || m_Row == null)
                return;
            m_Applied = true;
            m_On = m_Controller.IsOn;
            m_Row.alpha = m_On ? 1f : k_DisabledAlpha;
            m_Row.interactable = m_On;
            m_Row.blocksRaycasts = m_On;
        }
    }
}
