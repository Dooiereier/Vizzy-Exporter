using Assets.Scripts.CopyPaste.Export;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.CopyPaste.UI
{
    /// <summary>
    /// The "pick an existing Vizzy file to export into" popup shown after choosing
    /// "Export to..." from a block's right-click menu. Lists the .xml files found in the
    /// game's Vizzy programs folder; clicking one exports immediately and reports success
    /// or failure. Capped to a scrollable list so a large programs folder doesn't produce
    /// an unusably tall popup.
    /// </summary>
    public class ExportTargetPicker : MonoBehaviour
    {
        private const int MaxListedFiles = 40;
        private const float MaxListHeight = 320f;

        private static ExportTargetPicker _instance;

        private RectTransform _panel;
        private Transform _listParent;
        private ScrollRect _scrollRect;
        private VizzyExportSnippet _snippet;

        public static void Show(VizzyExportSnippet snippet, Vector2 screenPosition)
        {
            EnsureCreated();
            _instance.OpenAt(snippet, screenPosition);
        }

        private static void EnsureCreated()
        {
            if (_instance != null)
                return;

            GameObject go = new GameObject("VizzyExportTargetPicker");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<ExportTargetPicker>();
            _instance.Build();
            _instance.Hide();
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue; // above the context menu
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();

            // Click-away-to-close blocker, behind everything else.
            GameObject blockerGo = new GameObject("Blocker", typeof(RectTransform), typeof(Image), typeof(Button));
            blockerGo.transform.SetParent(transform, false);
            RectTransform blockerRect = blockerGo.GetComponent<RectTransform>();
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = Vector2.zero;
            blockerRect.offsetMax = Vector2.zero;
            blockerGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.15f);
            blockerGo.GetComponent<Button>().onClick.AddListener(Hide);

            GameObject panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelGo.transform.SetParent(transform, false);
            _panel = panelGo.GetComponent<RectTransform>();
            _panel.sizeDelta = new Vector2(260, 0);
            _panel.pivot = new Vector2(0f, 1f);

            panelGo.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.10f, 0.98f);

            VerticalLayoutGroup layout = panelGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 2;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            panelGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject headerGo = new GameObject("Header", typeof(RectTransform), typeof(LayoutElement));
            headerGo.transform.SetParent(panelGo.transform, false);
            headerGo.GetComponent<LayoutElement>().preferredHeight = 22;
            Text header = headerGo.AddComponent<Text>();
            header.text = "Export to which program?";
            header.font = PopupWidgets.ResolveDefaultFont();
            header.fontSize = 13;
            header.fontStyle = FontStyle.Bold;
            header.color = new Color(1, 1, 1, 0.85f);
            header.alignment = TextAnchor.MiddleLeft;

            // Scrollable list area: a fixed-height ScrollRect so a long programs folder
            // scrolls instead of pushing the popup off-screen. The list itself keeps the
            // same VerticalLayoutGroup + ContentSizeFitter as before, just now inside a
            // masked viewport instead of laid out directly in the panel.
            GameObject scrollGo = new GameObject("ScrollView", typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement));
            scrollGo.transform.SetParent(panelGo.transform, false);
            LayoutElement scrollLayoutElement = scrollGo.GetComponent<LayoutElement>();
            scrollLayoutElement.preferredHeight = MaxListHeight;
            scrollLayoutElement.flexibleHeight = 0;

            GameObject viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            RectTransform viewportRect = viewportGo.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject listGo = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listGo.transform.SetParent(viewportGo.transform, false);
            RectTransform listRect = listGo.GetComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0f, 1f);
            listRect.anchorMax = new Vector2(1f, 1f);
            listRect.pivot = new Vector2(0.5f, 1f);
            listRect.anchoredPosition = Vector2.zero;
            listRect.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup listLayout = listGo.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 1;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandHeight = false;
            listLayout.childForceExpandWidth = true;
            listGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scrollRect = scrollGo.GetComponent<ScrollRect>();
            _scrollRect.content = listRect;
            _scrollRect.viewport = viewportRect;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 24f;

            _listParent = listGo.transform;
        }

        private void OpenAt(VizzyExportSnippet snippet, Vector2 screenPosition)
        {
            _snippet = snippet;
            _panel.position = screenPosition;

            foreach (Transform child in _listParent)
                Destroy(child.gameObject);

            List<string> files = VizzyProgramFileExporter.ListProgramFiles();

            if (files.Count == 0)
            {
                PopupWidgets.CreateButton(_listParent, "(no program files found)", out Text emptyLabel).interactable = false;
                emptyLabel.color = new Color(1, 1, 1, 0.4f);
            }
            else
            {
                int shown = 0;
                foreach (string filePath in files)
                {
                    if (shown++ >= MaxListedFiles)
                    {
                        PopupWidgets.CreateButton(_listParent, $"... and {files.Count - MaxListedFiles} more", out Text moreLabel).interactable = false;
                        moreLabel.color = new Color(1, 1, 1, 0.4f);
                        break;
                    }

                    string capturedPath = filePath;
                    string displayName = Path.GetFileNameWithoutExtension(filePath);
                    Button fileButton = PopupWidgets.CreateButton(_listParent, displayName, out _);
                    fileButton.onClick.AddListener(() => OnFileChosen(capturedPath));
                }
            }

            gameObject.SetActive(true);

            // Force the layout to settle before snapping scroll to the top, otherwise
            // verticalNormalizedPosition reads against last frame's (stale/zero) content size.
            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = 1f;
        }

        private void OnFileChosen(string targetFilePath)
        {
            VizzyExportSnippet snippet = _snippet;
            Hide();

            if (VizzyProgramFileExporter.TryExport(snippet, targetFilePath, out ExportCounts counts, out string error))
            {
                List<string> createdNotes = new List<string>();
                AppendCountNote(createdNotes, counts.VariablesCreated, "global variable", "global variables");
                AppendCountNote(createdNotes, counts.CustomExpressionsCreated, "custom expression", "custom expressions");
                AppendCountNote(createdNotes, counts.CustomInstructionsCreated, "custom instruction", "custom instructions");

                string createdNote = createdNotes.Count > 0
                    ? $" Created {string.Join(" and ", createdNotes)} it needed."
                    : "";

                PopupWidgets.ShowMessage($"Exported to {Path.GetFileNameWithoutExtension(targetFilePath)}." +
                                          createdNote +
                                          " It'll show up as a new block group next time that program is opened.");
            }
            else
            {
                PopupWidgets.ShowMessage(error ?? "Export failed.");
            }
        }

        private void Hide()
        {
            gameObject.SetActive(false);
            _snippet = null;
        }

        private static void AppendCountNote(List<string> notes, int count, string singular, string plural)
        {
            if (count == 1)
                notes.Add($"1 {singular}");
            else if (count > 1)
                notes.Add($"{count} {plural}");
        }
    }
}
