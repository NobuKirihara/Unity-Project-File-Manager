using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace projectfilemanager
{
    public class UnusedAssetFinder : EditorWindow
    {
        // Constants & Private Fields
        private const string UnusedFolder = "Assets/Unused";
        private const string WindowTitle = "Project File Manager";
        private const string PrefsKey_Extensions = "UAF_Extensions";
        private const string PrefsKey_ScanPath = "UAF_ScanPath";

        private static readonly string[] DefaultExtensions =
        {
        ".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr", ".hdr", ".cubemap",
        ".mat", ".shader", ".shadergraph", ".compute",
        ".fbx", ".obj", ".prefab", ".unity", ".asset", ".anim", ".controller",
        ".vfx", ".vfxgraph", ".cs", ".txt", ".json"
    };

        private GUIStyle _rowEvenStyle, _rowOddStyle, _tagStyle;
        private bool _stylesInit;

        private int _currentTab = 0;
        private readonly string[] _tabNames = { "Unused Finder", "Size Explorer" };

        private string _extensionInput = "";
        private string _scanPath = "Assets";
        private string _targetFolderName = "";
        private string _searchFilter = "";
        private Vector2 _scrollPos;
        private bool _isScanning;
        private string _statusMessage = "Ready.";

        private List<AssetEntry> _unusedResults = new();
        private List<AssetEntry> _sizeExplorerResults = new();
        private bool _onlySelected;
        private bool _sortDescending = true;

        [MenuItem("Tools/Project File Manager")]
        public static void OpenWindow() => GetWindow<UnusedAssetFinder>(WindowTitle).Show();

        private void OnEnable()
        {
            _extensionInput = EditorPrefs.GetString(PrefsKey_Extensions, string.Join(", ", DefaultExtensions));
            _scanPath = EditorPrefs.GetString(PrefsKey_ScanPath, "Assets");
        }

        private void OnGUI()
        {
            InitStyles();

            EditorGUILayout.Space(10);
            _currentTab = GUILayout.Toolbar(_currentTab, _tabNames, GUILayout.Height(25));
            EditorGUILayout.Space(10);

            if (_currentTab == 0) DrawUnusedFinderTab();
            else DrawSizeExplorerTab();

            GUILayout.FlexibleSpace();
            DrawStatusBar();
        }

        // Unused Finder
        private void DrawUnusedFinderTab()
        {
            EditorGUILayout.HelpBox("Finds assets not referenced in the project (ignores 'Editor' folders).", MessageType.Info);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Scan Settings", EditorStyles.boldLabel);
            DrawPathSelector("Source Folder:", ref _scanPath, "Select Source Folder");
            DrawPathSelector("Target Subfolder:", ref _targetFolderName, "Select Target Folder", true);
            _extensionInput = EditorGUILayout.TextField("Extensions:", _extensionInput);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);
            DrawUnusedActions();
            DrawAssetTable(_unusedResults, true);
        }

        private void DrawUnusedActions()
        {
            EditorGUILayout.BeginHorizontal();

            // Scan Button
            GUI.enabled = !_isScanning;
            if (GUILayout.Button("Scan Unused", GUILayout.Height(28))) StartUnusedScan();

            // Selection Helpers
            GUI.enabled = !_isScanning && _unusedResults.Count > 0;
            if (GUILayout.Button(" Select All", GUILayout.Width(80), GUILayout.Height(28))) ToggleAll(true);
            if (GUILayout.Button("None", GUILayout.Width(45), GUILayout.Height(28))) ToggleAll(false);

            // Action Buttons
            var count = _unusedResults.Count(r => r.Checked);
            GUI.enabled = !_isScanning && count > 0;

            GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
            if (GUILayout.Button($"Move ({count})", GUILayout.Height(28))) MoveAssets();

            GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
            if (GUILayout.Button($"Delete ({count})", GUILayout.Height(28))) DeleteAssets();

            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void ToggleAll(bool state)
        {
            foreach (var entry in _unusedResults) entry.Checked = state;
        }
       

        // Size Explorer
        private void DrawSizeExplorerTab()
        {
            EditorGUILayout.HelpBox("Analyze file sizes across the entire project Assets folder.", MessageType.Info);

            if (GUILayout.Button("Scan All Project Sizes", GUILayout.Height(28))) StartSizeScan();

            DrawAssetTable(_sizeExplorerResults, false);
        }
       

        // Shared UI Components
        private void DrawAssetTable(List<AssetEntry> list, bool showActions)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            _searchFilter = EditorGUILayout.TextField("Filter Name:", _searchFilter);
            if (showActions) _onlySelected = GUILayout.Toggle(_onlySelected, "Selected only", GUILayout.Width(110));
            EditorGUILayout.EndHorizontal();

            var filtered = list
                .Where(r => string.IsNullOrEmpty(_searchFilter) || r.Path.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(r => !showActions || !_onlySelected || r.Checked).ToList();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (showActions) GUILayout.Label("+", EditorStyles.toolbarButton, GUILayout.Width(24));
            GUILayout.Label("Type", EditorStyles.toolbarButton, GUILayout.Width(44));
            GUILayout.Label("Asset Path", EditorStyles.toolbarButton, GUILayout.MinWidth(200));

            if (GUILayout.Button("Size " + (_sortDescending ? "▼" : "▲"), EditorStyles.toolbarButton, GUILayout.Width(85)))
            {
                _sortDescending = !_sortDescending;
                list.Sort((a, b) => _sortDescending ? b.SizeBytes.CompareTo(a.SizeBytes) : a.SizeBytes.CompareTo(b.SizeBytes));
            }
            GUILayout.Label("Ping", EditorStyles.toolbarButton, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            for (int i = 0; i < filtered.Count; i++)
            {
                var e = filtered[i];
                EditorGUILayout.BeginHorizontal(i % 2 == 0 ? _rowEvenStyle : _rowOddStyle);

                if (showActions) e.Checked = GUILayout.Toggle(e.Checked, "", GUILayout.Width(24));

                DrawTypeTag(e.Extension);

                if (GUILayout.Button(e.Path, EditorStyles.label, GUILayout.MinWidth(200)))
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(e.Path);

                GUILayout.Label(FormatSize(e.SizeBytes), GUILayout.Width(80));

                if (GUILayout.Button(">", GUILayout.Width(30)))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(e.Path));

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawPathSelector(string label, ref string pathValue, string title, bool isFolderOnly = false)
        {
            EditorGUILayout.BeginHorizontal();
            pathValue = EditorGUILayout.TextField(label, pathValue);
            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFolderPanel(title, isFolderOnly ? UnusedFolder : pathValue, "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (isFolderOnly) pathValue = Path.GetFileName(path);
                    else pathValue = "Assets" + path.Replace(Application.dataPath, "").Replace('\\', '/');
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTypeTag(string ext)
        {
            GUI.backgroundColor = GetTypeColor(ext);
            GUILayout.Label(GetTypeLabel(ext), _tagStyle, GUILayout.Width(44));
            GUI.backgroundColor = Color.white;
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(_statusMessage, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }       

        // Scanning & File Operations
        private void StartUnusedScan()
        {
            _isScanning = true; _unusedResults.Clear();
            try
            {
                var exts = new HashSet<string>(_extensionInput.Split(',').Select(s => s.Trim().ToLower()).Select(s => s.StartsWith(".") ? s : "." + s));
                _unusedResults = PerformScan(true, exts, _scanPath);
                _statusMessage = $"Scan complete: {_unusedResults.Count} unused assets found.";
            }
            finally { _isScanning = false; EditorUtility.ClearProgressBar(); }
        }

        private void StartSizeScan()
        {
            _isScanning = true; _sizeExplorerResults.Clear();
            try
            {
                _sizeExplorerResults = PerformScan(false, null, "Assets");
                _sizeExplorerResults.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
                _statusMessage = $"Size scan complete: {_sizeExplorerResults.Count} files indexed.";
            }
            finally { _isScanning = false; EditorUtility.ClearProgressBar(); }
        }

        private List<AssetEntry> PerformScan(bool checkReferences, HashSet<string> extensions, string root)
        {
            var allPaths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith(root) && !AssetDatabase.IsValidFolder(p)).ToList();

            if (checkReferences)
            {
                allPaths = allPaths.Where(p => !p.Contains("/Editor/") && !p.StartsWith(UnusedFolder) && extensions.Contains(Path.GetExtension(p).ToLower())).ToList();
                var referencedGuids = new HashSet<string>();
                var sourceFiles = AssetDatabase.GetAllAssetPaths().Where(p => new[] { ".unity", ".prefab", ".mat", ".asset", ".anim", ".controller", ".cs", ".shadergraph", ".vfx" }.Contains(Path.GetExtension(p).ToLower())).ToList();

                for (int i = 0; i < sourceFiles.Count; i++)
                {
                    if (i % 50 == 0) EditorUtility.DisplayProgressBar(WindowTitle, "Verifying references...", (float)i / sourceFiles.Count);
                    try { ExtractGuids(File.ReadAllText(sourceFiles[i]), referencedGuids); } catch { }
                }
                return allPaths.Select(p => new AssetEntry(p)).Where(e => !referencedGuids.Contains(e.Guid)).ToList();
            }
            return allPaths.Select(p => new AssetEntry(p)).ToList();
        }

        private void MoveAssets()
        {
            var toMove = _unusedResults.Where(r => r.Checked).ToList();
            string targetPath = UnusedFolder + "/" + (string.IsNullOrEmpty(_targetFolderName) ? "Default" : _targetFolderName);
            if (!AssetDatabase.IsValidFolder(targetPath))
            {
                if (!AssetDatabase.IsValidFolder(UnusedFolder)) AssetDatabase.CreateFolder("Assets", "Unused");
                AssetDatabase.CreateFolder(UnusedFolder, Path.GetFileName(targetPath));
            }

            AssetDatabase.StartAssetEditing();
            foreach (var entry in toMove) AssetDatabase.MoveAsset(entry.Path, targetPath + "/" + Path.GetFileName(entry.Path));
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
            StartUnusedScan();
        }

        private void DeleteAssets()
        {
            var toDelete = _unusedResults.Where(r => r.Checked).ToList();
            if (EditorUtility.DisplayDialog("Delete Assets", $"Permanently delete {toDelete.Count} assets?", "Delete", "Cancel"))
            {
                AssetDatabase.StartAssetEditing();
                foreach (var entry in toDelete) AssetDatabase.DeleteAsset(entry.Path);
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
                StartUnusedScan();
            }
        }

        // Styles & Metadata
        private void InitStyles()
        {
            if (_stylesInit) return; _stylesInit = true;
            _rowEvenStyle = new GUIStyle { normal = { background = MakeTex(2, 2, new Color(0.22f, 0.22f, 0.22f, 0.45f)) }, padding = new RectOffset(2, 2, 2, 2) };
            _rowOddStyle = new GUIStyle { normal = { background = MakeTex(2, 2, new Color(0.17f, 0.17f, 0.17f, 0.45f)) }, padding = new RectOffset(2, 2, 2, 2) };
            _tagStyle = new GUIStyle(EditorStyles.miniButtonMid) { fontSize = 9, alignment = TextAnchor.MiddleCenter };
        }

        private Texture2D MakeTex(int w, int h, Color col) { var pix = new Color[w * h]; for (int i = 0; i < pix.Length; i++) pix[i] = col; var tex = new Texture2D(w, h); tex.SetPixels(pix); tex.Apply(); return tex; }

        private static string GetTypeLabel(string ext) => ext switch
        {
            ".png" or ".jpg" or ".tga" or ".psd" => "IMG",
            ".exr" or ".hdr" => "SKY",
            ".cubemap" => "CUBE",
            ".mat" => "MAT",
            ".shader" or ".shadergraph" or ".compute" => "SHD",
            ".fbx" or ".obj" => "3D",
            ".prefab" => "PRE",
            ".unity" => "SCN",
            ".vfx" or ".vfxgraph" => "VFX",
            ".cs" => "CS",
            _ => "???"
        };

        private static Color GetTypeColor(string ext) => ext switch
        {
            ".png" or ".jpg" or ".tga" => new Color(0.40f, 0.80f, 1.00f),
            ".exr" or ".hdr" => new Color(1.00f, 0.90f, 0.20f),
            ".cubemap" => new Color(0.70f, 0.50f, 1.00f),
            ".mat" => new Color(0.60f, 1.00f, 0.60f),
            ".shader" or ".shadergraph" => new Color(1.00f, 0.85f, 0.40f),
            ".fbx" or ".obj" => new Color(1.00f, 0.65f, 0.40f),
            ".prefab" => new Color(0.60f, 0.90f, 1.00f),
            ".vfx" or ".vfxgraph" => new Color(0.40f, 1.00f, 0.90f),
            ".cs" => new Color(1.00f, 0.75f, 0.75f),
            _ => new Color(0.75f, 0.75f, 0.75f)
        };

        private static string FormatSize(long b) => b < 1048576 ? $"{b / 1024f:F1} KB" : $"{b / 1048576f:F1} MB";

        private void ExtractGuids(string text, HashSet<string> target)
        {
            int idx = 0; const string t = "guid: ";
            while ((idx = text.IndexOf(t, idx, StringComparison.Ordinal)) >= 0)
            {
                idx += t.Length; if (idx + 32 <= text.Length) target.Add(text.Substring(idx, 32));
            }
        }

        private class AssetEntry
        {
            public string Path, Extension, Guid; public long SizeBytes; public bool Checked;
            public AssetEntry(string path)
            {
                Path = path; Extension = System.IO.Path.GetExtension(path).ToLower();
                Guid = AssetDatabase.AssetPathToGUID(path);
                var info = new FileInfo(Application.dataPath + path.Substring(6));
                SizeBytes = info.Exists ? info.Length : 0;
            }
        }
    }
}