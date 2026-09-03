using Assets.Scripts.CopyPaste.Export;
using Assets.Scripts.Vizzy.UI.Elements;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.CopyPaste.UI
{
    /// <summary>
    /// The right-click popup on a block. Currently just one entry, "Export to...", which
    /// hands off to ExportTargetPicker to choose the destination file. Built at runtime out
    /// of plain UnityEngine.UI primitives rather than the game's own XML UI layout system,
    /// so this mod doesn't need to understand that system's internals.
    /// </summary>
    public class BlockContextMenu : MonoBehaviour
    {
        private static BlockContextMenu _instance;
        private static float _lastOpenTime = -1f;

        private RectTransform _panel;
        private Button _exportButton;

        private BlockElementScript _targetBlock;

        public static void EnsureCreated()
        {
            if (_instance != null)
                return;

            GameObject go = new GameObject("VizzyCopyPasteContextMenu");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<BlockContextMenu>();
            _instance.Build();
            _instance.Hide();
        }

        /// <summary>
        /// Shows the menu for a right-clicked block. Debounced so that if more than one
        /// Harmony patch happens to fire for the same physical click, we still only open one
        /// menu (see ContextMenuPatcher for why more than one class can be patched).
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
            canvas.sortingOrder = short.MaxValue - 1; // below the file picker, above the game's own UI
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panelGo.transform.SetParent(transform, false);
            _panel = panelGo.GetComponent<RectTransform>();
            _panel.sizeDelta = new Vector2(160, 0);
            _panel.pivot = new Vector2(0f, 1f);

            panelGo.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.10f, 0.96f);

            VerticalLayoutGroup layout = panelGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 2;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            ContentSizeFitter fitter = panelGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _exportButton = PopupWidgets.CreateButton(panelGo.transform, "Export to...", out _);
            _exportButton.onClick.AddListener(OnExportClicked);

            // Click-away-to-close: an invisible full-screen button behind the panel.
            GameObject blockerGo = new GameObject("Blocker", typeof(RectTransform), typeof(Image), typeof(Button));
            blockerGo.transform.SetParent(transform, false);
            blockerGo.transform.SetAsFirstSibling();
            RectTransform blockerRect = blockerGo.GetComponent<RectTransform>();
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = Vector2.zero;
            blockerRect.offsetMax = Vector2.zero;
            blockerGo.GetComponent<Image>().color = new Color(0, 0, 0, 0); // transparent, just eats the click
            blockerGo.GetComponent<Button>().onClick.AddListener(Hide);
        }

        private void OpenAt(BlockElementScript block, Vector2 screenPosition)
        {
            _targetBlock = block;
            _panel.position = screenPosition;

            bool canExport = block != null && block.Node != null;
            _exportButton.interactable = canExport;

            gameObject.SetActive(true);
        }

        private void Hide()
        {
            gameObject.SetActive(false);
        }

        private void OnExportClicked()
        {
            BlockElementScript block = _targetBlock;
            Vector2 position = _panel.position;

            Hide();

            if (!VizzyExportSnippet.TryCapture(block, out VizzyExportSnippet snippet, out string error))
            {
                PopupWidgets.ShowMessage(error ?? "Nothing to export.");
                return;
            }

            ExportTargetPicker.Show(snippet, position);
        }
    }
}
