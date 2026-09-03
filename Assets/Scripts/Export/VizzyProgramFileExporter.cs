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

        // A block's on-canvas position is a "pos" attribute holding "X,Y" plain integers,
        // and it only ever appears on the *first* element of a top-level block - confirmed
        // against kroryan/VizzyCode's VizzyXmlConverter.cs (EmitTopLevelBlockMetadata reads
        // firstEl.Attribute("pos"); AssignNodeIdsRecursive/EmitVariableDeclarations-adjacent
        // layout code only ever writes "pos" onto the first instruction of each block, or
        // onto each top-level CustomExpression). The same source also confirms the program
        // root holds *multiple* sibling <Instructions> elements - one per independent
        // top-level column/script (`program.Elements("Instructions")`, plural) - each
        // containing its own flat, ordered chain of instruction siblings, versus a *single*
        // shared <Expressions> element (`program.Element("Expressions")`, singular) holding
        // all standalone expression-family entries as flat siblings.
        //
        // That mattered for a real bug: an earlier version of this method merged an
        // exported instruction chain into the target's *existing* first <Instructions>
        // block, which would have spliced the pasted blocks into whatever column already
        // lived there instead of creating an independent new one. Instruction exports now
        // always get their own brand new <Instructions> sibling.
        private const string PosAttributeName = "pos";
        private static readonly System.Random JitterRandom = new System.Random();

        /// <summary>
        /// Writes a captured snippet into the target file as a new top-level block
        /// (a whole new column for instructions; a new flat entry in the shared Expressions
        /// container for a lone expression), positions it near the middle of the target's
        /// existing content so it lands somewhere visible instead of off in a stale
        /// coordinate copied from the source program, creates any global variables it
        /// references that the target doesn't already have, and saves the file.
        /// </summary>
        public static bool TryExport(VizzyExportSnippet snippet, string targetFilePath, out int variablesCreated, out string error)
        {
            error = null;
            variablesCreated = 0;

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

                // Deep-copy every node before touching it - the source XElements may already
                // be parented (e.g. under the throwaway scratch element SerializerBridge
                // uses), and an XElement can't be added as a child in two places at once.
                List<XElement> copiedNodes = snippet.Nodes.Select(n => new XElement(n)).ToList();
                foreach (XElement node in copiedNodes)
                {
                    StripIds(node); // avoid colliding with node ids already used in the target file
                }

                (int centerX, int centerY) = ComputeCenterPosition(root);
                SetPosition(copiedNodes[0], centerX, centerY);

                if (snippet.Kind == VizzyNodeKind.Instruction)
                {
                    // A whole new independent column - never merged into an existing
                    // <Instructions> block (see the note above on why that would be wrong).
                    XElement newBlock = new XElement("Instructions");
                    foreach (XElement node in copiedNodes)
                        newBlock.Add(node);
                    root.Add(newBlock);
                }
                else
                {
                    XElement expressions = root.Element("Expressions");
                    if (expressions == null)
                    {
                        expressions = new XElement("Expressions");
                        root.Add(expressions);
                    }

                    foreach (XElement node in copiedNodes)
                        expressions.Add(node);
                }

                variablesCreated = AddMissingVariables(root, snippet.ReferencedGlobalVariableDefinitions);

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

        /// <summary>
        /// Approximates "the middle of the Vizzy screen" for the target file by averaging
        /// the position of its existing top-level blocks - we have no way to read the
        /// actual camera/scroll state of a program that isn't currently open in the editor,
        /// but a file's existing content is generally where the user's view already sits,
        /// so its centroid is a reasonable stand-in. Falls back to the origin for an
        /// empty/new-ish file. A small random jitter is added so exporting more than once
        /// into the same file doesn't stack every export exactly on top of the last one.
        /// </summary>
        private static (int x, int y) ComputeCenterPosition(XElement root)
        {
            List<(int x, int y)> positions = new List<(int x, int y)>();

            foreach (XElement block in root.Elements("Instructions"))
            {
                XElement head = block.Elements().FirstOrDefault();
                if (TryGetPosition(head, out int x, out int y))
                    positions.Add((x, y));
            }

            XElement expressionsContainer = root.Element("Expressions");
            if (expressionsContainer != null)
            {
                foreach (XElement expr in expressionsContainer.Elements())
                {
                    if (TryGetPosition(expr, out int x, out int y))
                        positions.Add((x, y));
                }
            }

            int baseX = 0;
            int baseY = 0;
            if (positions.Count > 0)
            {
                baseX = (int)positions.Average(p => p.x);
                baseY = (int)positions.Average(p => p.y);
            }

            const int jitterRange = 50;
            baseX += JitterRandom.Next(-jitterRange, jitterRange + 1);
            baseY += JitterRandom.Next(-jitterRange, jitterRange + 1);

            return (baseX, baseY);
        }

        private static bool TryGetPosition(XElement element, out int x, out int y)
        {
            x = 0;
            y = 0;

            string value = (string)element?.Attribute(PosAttributeName);
            if (string.IsNullOrEmpty(value))
                return false;

            string[] parts = value.Split(',');
            return parts.Length == 2 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y);
        }

        private static void SetPosition(XElement element, int x, int y)
        {
            element.SetAttributeValue(PosAttributeName, $"{x},{y}");
        }

        /// <summary>
        /// Removes "id" attributes from a node and everything nested inside it. Ids appear
        /// to be assigned per-file (kroryan/VizzyCode's own XML generator hands out fresh
        /// sequential ids and explicitly avoids colliding with whatever ids already exist),
        /// so carrying a source file's ids into a different target file risks colliding with
        /// unrelated nodes that happen to reuse the same numbers there. Stripping them is
        /// the conservative choice - a freshly-dragged-in toolbox block presumably doesn't
        /// arrive with a pre-assigned id either, so the game should be able to assign new
        /// ones on its own next load/save.
        /// </summary>
        private static void StripIds(XElement node)
        {
            node.Attribute("id")?.Remove();
            foreach (XElement descendant in node.Descendants())
            {
                descendant.Attribute("id")?.Remove();
            }
        }

        /// <summary>
        /// Adds a copy of each given variable definition to the target's Variables
        /// container, skipping any name the target already defines (never overwrites an
        /// existing variable - a same-named variable already in the target might be a
        /// different type/value on purpose, and blindly replacing it could break other
        /// blocks there that rely on it). Returns how many were actually added.
        /// </summary>
        private static int AddMissingVariables(XElement root, List<XElement> definitions)
        {
            if (definitions == null || definitions.Count == 0)
                return 0;

            XElement variables = root.Element("Variables");
            if (variables == null)
            {
                variables = new XElement("Variables");
                root.Add(variables);
            }

            HashSet<string> existingNames = new HashSet<string>(
                variables.Elements("Variable").Select(v => (string)v.Attribute("name")).Where(n => n != null),
                StringComparer.Ordinal); // Vizzy is case-sensitive

            int added = 0;
            foreach (XElement definition in definitions)
            {
                string name = (string)definition.Attribute("name");
                if (string.IsNullOrEmpty(name) || existingNames.Contains(name))
                    continue;

                variables.Add(new XElement(definition));
                existingNames.Add(name);
                added++;
            }

            return added;
        }
    }
}
