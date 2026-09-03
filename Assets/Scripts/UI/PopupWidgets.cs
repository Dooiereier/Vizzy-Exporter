using System;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.CopyPaste.UI
{
    /// <summary>Small shared helpers for the runtime-built popups (context menu, file picker).</summary>
    internal static class PopupWidgets
    {
        public static Button CreateButton(Transform parent, string label, out Text text)
        {
            GameObject buttonGo = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGo.transform.SetParent(parent, false);
            buttonGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.06f);
            buttonGo.GetComponent<LayoutElement>().preferredHeight = 26;

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(buttonGo.transform, false);
            RectTransform textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8, 0);
            textRect.offsetMax = new Vector2(-8, 0);

            text = textGo.AddComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.font = ResolveDefaultFont();
            text.fontSize = 14;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            return buttonGo.GetComponent<Button>();
        }

        public static Font ResolveDefaultFont()
        {
            // Unity renamed the builtin font a few times across versions; try the known names.
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        public static void ShowMessage(string text)
        {
            try
            {
                Game.Instance.UserInterface.CreateMessageDialog(text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Vizzy Copy Paste] {text} (also failed to show in-game dialog: {ex.Message})");
            }
        }
    }
}
