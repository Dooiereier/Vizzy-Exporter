using Assets.Scripts.Vizzy.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace Assets.Scripts.CopyPaste.Export
{
    /// <summary>
    /// Lists existing Vizzy program files and writes a captured snippet into one of them.
    /// This never touches the running game's in-memory state - it edits the target .xml
    /// file directly on disk, the same file format ProgramSerializer itself reads and
    /// writes, so nothing here needs to know how to build on-screen blocks.
    /// </summary>
    public static class VizzyProgramFileExporter
    {
        /// <summary>
        /// Where the game keeps saved/importable Vizzy programs. Confirmed field - Vizzy
        /// Studio's own Mod.cs reads this same static path to back up program files.
        /// </summary>
        public static string ProgramsFolder => VizzyUIScript.FlightProgramsFolderPath;

        public static List<string> ListProgramFiles()
        {
            try
            {
                if (string.IsNullOrEmpty(ProgramsFolder) || !Directory.Exists(ProgramsFolder))
                {
                    Debug.LogWarning($"[Vizzy Copy Paste] Programs folder not found: {ProgramsFolder}");
                    return new List<string>();
                }

                return Directory.GetFiles(ProgramsFolder, "*.xml")
                    .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Vizzy Copy Paste] Couldn't list program files: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Appends a captured snippet's nodes into the target file's Instructions (or
        /// Expressions) container as a new top-level column, and saves the file. Confirmed:
        /// root-level node containers are literally named "Instructions"/"Expressions"
        /// (ProgramSerializerPatches checks `nodeElement.Parent.Name == "Instructions" ||
        /// ... == "Expressions"` in the reference mod this was built from).
        /// </summary>
        public static bool TryExport(VizzyExportSnippet snippet, string targetFilePath, out string error)
        {
            error = null;

            if (snippet == null || snippet.Nodes.Count == 0)
            {
                error = "Nothing to export.";
                return false;
            }

            try
            {
                if (!File.Exists(targetFilePath))
                {
                    error = "That file no longer exists.";
                    return false;
                }

                XDocument doc = XDocument.Load(targetFilePath);
                XElement root = doc.Root;
                if (root == null)
                {
                    error = "That file doesn't look like a valid Vizzy program.";
                    return false;
                }

                string containerName = snippet.Kind == VizzyNodeKind.Instruction ? "Instructions" : "Expressions";
                XElement container = root.Element(containerName);
                if (container == null)
                {
                    container = new XElement(containerName);
                    root.Add(container);
                }

                // Deep-copy each node before attaching - the source XElement may already be
                // parented (e.g. under the throwaway scratch element SerializerBridge uses),
                // and XElement can't be added as a child in two places at once.
                foreach (XElement node in snippet.Nodes)
                {
                    container.Add(new XElement(node));
                }

                doc.Save(targetFilePath);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Couldn't export into that file: {ex.Message}";
                Debug.LogError($"[Vizzy Copy Paste] {error}\n{ex}");
                return false;
            }
        }
    }
}
