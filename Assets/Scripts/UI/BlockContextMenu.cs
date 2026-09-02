using Assets.Scripts.CopyPaste.Bridge;
using Assets.Scripts.CopyPaste.Clipboard;
using Assets.Scripts.Vizzy.UI.Elements;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.CopyPaste.UI
{
    /// <summary>
    /// A minimal, self-drawn right-click menu with Copy/Paste entries. It's built at
    /// runtime out of plain UnityEngine.UI primitives on its own overlay Canvas rather
    /// than through the game's XML UI layout system, on purpose: it means this mod
    /// doesn't need to understand that system's internals to add a menu, at the cost of
    /// looking a little plainer than a native panel. Feel free to reskin CreateButton()
    /// once this is working end to end.
    /// </summary>
    public class BlockContextMenu : MonoBehaviour
    {
        private static BlockContextMenu _instance;
        private static float _lastOpenTime = -1f;

        private RectTransform _panel;
        private Button _copyButton;
        private Button _pasteButton;
        private Text _copyLabel;
        private Text _pasteLabel;

        private BlockElementScript _targetBlock;

        public static void EnsureCreated()
        {
            if (_instance != null)
                return;

            GameObject go = new GameObject("VizzyCopyPasteContextMenu");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<BlockContextMenu>();
            _instance.Build();
            _instance.Hide();
        }

        /// <summary>
        /// Shows the menu for a right-clicked block. Debounced so that if both a base-class
        /// and subclass Harmony patch happen to fire for the same physical click, we still
        /// only open one menu.
        /// </summary>
        public static void ShowFor(BlockElementScript block, Vector2 screenPosition)
        {
            if (Time.unscaledTime - _lastOpenTime < 0.05f)
                return;
            _lastOpenTime = Time.unscaledTime;

            EnsureCreated();
            _instance.OpenAt(block, screenPosition);
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue; // draw above the game's own UI
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panelGo.transform.SetParent(transform, false);
            _panel = panelGo.GetComponent<RectTransform>();
            _panel.sizeDelta = new Vector2(160, 0);
            _panel.pivot = new Vector2(0f, 1f);

            Image panelImage = panelGo.GetComponent<Image>();
            panelImage.color = new Color(0.10f, 0.10f, 0.10f, 0.96f);

            VerticalLayoutGroup layout = panelGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 2;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            ContentSizeFitter fitter = panelGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _copyButton = CreateButton(panelGo.transform, "Copy", out _copyLabel);
            _pasteButton = CreateButton(panelGo.transform, "Paste", out _pasteLabel);

            _copyButton.onClick.AddListener(OnCopyClicked);
            _pasteButton.onClick.AddListener(OnPasteClicked);

            // Click-away-to-close: an invisible full-screen button behind the panel.
            GameObject blockerGo = new GameObject("Blocker", typeof(RectTransform), typeof(Image), typeof(Button));
            blockerGo.transform.SetParent(transform, false);
            blockerGo.transform.SetAsFirstSibling();
            RectTransform blockerRect = blockerGo.GetComponent<RectTransform>();
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = Vector2.zero;
            blockerRect.offsetMax = Vector2.zero;
            Image blockerImage = blockerGo.GetComponent<Image>();
            blockerImage.color = new Color(0, 0, 0, 0); // fully transparent, just eats the click
            blockerGo.GetComponent<Button>().onClick.AddListener(Hide);
        }

        private Button CreateButton(Transform parent, string label, out Text text)
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

            return buttonGo.GetComponent<Button>();
        }

        private static Font ResolveDefaultFont()
        {
            // Unity renamed the builtin font a few times across versions; try the known names.
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        private void OpenAt(BlockElementScript block, Vector2 screenPosition)
        {
            _targetBlock = block;

            _panel.position = screenPosition;

            bool canCopy = block != null && block.Node != null;
            bool canPaste = VizzyClipboard.ClipboardHasVizzyPayload();

            _copyButton.interactable = canCopy;
            _pasteButton.interactable = canPaste;
            _copyLabel.color = canCopy ? Color.white : new Color(1, 1, 1, 0.35f);
            _pasteLabel.color = canPaste ? Color.white : new Color(1, 1, 1, 0.35f);

            gameObject.SetActive(true);
        }

        private void Hide()
        {
            gameObject.SetActive(false);
            _targetBlock = null;
        }

        private void OnCopyClicked()
        {
            if (_targetBlock != null && VizzyClipboard.TryCopy(_targetBlock, out string error))
            {
                // Copied fine - nothing further to do, the OS clipboard now holds the snippet.
            }
            else if (error != null)
            {
                ShowMessage(error);
            }

            Hide();
        }

        private void OnPasteClicked()
        {
            Vector2 pastePosition = _panel.position;
            BlockElementScript anchorBlock = _targetBlock;

            Hide();

            if (!VizzyClipboard.TryReadClipboard(out VizzyClipboardPayload payload, out string readError))
            {
                ShowMessage(readError ?? "Nothing to paste.");
                return;
            }

            if (anchorBlock == null)
            {
                ShowMessage("Right-click an existing block first so I know which editor to paste into.");
                return;
            }

            if (!NodeBuilderBridge.TryBuildAndBeginDrag(anchorBlock, payload.Root, pastePosition, out string buildError))
            {
                ShowMessage(
                    "Paste couldn't place the block automatically on this game version " +
                    $"({buildError}). Nothing was lost - it's still on your clipboard. " +
                    "See docs/IMPLEMENTATION_NOTES.md to point NodeBuilderBridge at the right method.");
            }
        }

        private static void ShowMessage(string text)
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
