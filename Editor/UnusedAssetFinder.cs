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
        // Data Structures
        private class AssetEntry
        {
            public string Path { get; }
            public string Extension { get; }
            public string Guid { get; }
            public long SizeBytes { get; }
            public bool Checked;

            public AssetEntry(string path)
            {
                Path = path;
                Extension = System.IO.Path.GetExtension(path).ToLower();
                Guid = AssetDatabase.AssetPathToGUID(path);

                var info = new FileInfo(Application.dataPath + path.Substring(6));
                SizeBytes = info.Exists ? info.Length : 0;
            }
        }

        // Constants & Private Fields
        private const string UnusedFolderPath = "Assets/Unused";
        private const string PrefsKey_Extensions = "UAF_Extensions";
        private const string PrefsKey_ScanPath = "UAF_ScanPath";

        private static readonly string[] DefaultExtensions =
        {
        ".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr", ".hdr", ".cubemap",
        ".mat", ".shader", ".shadergraph", ".compute",
        ".fbx", ".obj", ".prefab", ".unity", ".asset", ".anim", ".controller",
        ".vfx", ".vfxgraph", ".cs", ".txt", ".json"
    };

        // UI Styles
        private GUIStyle _rowEvenStyle, _rowOddStyle, _tagStyle;
        private bool _stylesInitialized;

        // State
        private int _currentTab;
        private string _extensionInput = "";
        private string _scanPath = "Assets";
        private string _targetFolderName = "";
        private string _searchFilter = "";
        private Vector2 _scrollPos;
        private bool _isScanning;
        private string _statusMessage = "Ready.";

        private List<AssetEntry> _unusedResults = new List<AssetEntry>();
        private List<AssetEntry> _sizeExplorerResults = new List<AssetEntry>();
        private bool _showSelectedOnly;
        private bool _sortDescending = true;
    

        // Editor Window 
        [MenuItem("Tools/Project File Manager")]
        public static void OpenWindow() => GetWindow<UnusedAssetFinder>("Project File Manager").Show();

        private void OnEnable()
        {
            _extensionInput = EditorPrefs.GetString(PrefsKey_Extensions, string.Join(", ", DefaultExtensions));
            _scanPath = EditorPrefs.GetString(PrefsKey_ScanPath, "Assets");
        }
    
        // GUI Rendering
        private void OnGUI()
        {
            InitializeStyles();

            EditorGUILayout.Space(10);
            _currentTab = GUILayout.Toolbar(_currentTab, new[] { "Unused Finder", "Size Explorer" }, GUILayout.Height(25));
            EditorGUILayout.Space(10);

            if (_currentTab == 0) DrawUnusedFinderTab();
            else DrawSizeExplorerTab();

            GUILayout.FlexibleSpace();
            DrawStatusBar();
        }

        private void DrawUnusedFinderTab()
        {
            EditorGUILayout.HelpBox("Finds assets not referenced in the project (ignores 'Editor' folders).", MessageType.Info);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            DrawPathField("Source Folder:", ref _scanPath, "Select Source", false);
            DrawPathField("Target Subfolder:", ref _targetFolderName, "Select Target", true);
            _extensionInput = EditorGUILayout.TextField("Extensions:", _extensionInput);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);
            DrawActionButtonGroup();
            DrawAssetTable(_unusedResults, true);
        }

        private void DrawSizeExplorerTab()
        {
            EditorGUILayout.HelpBox("Global file size analysis across the Assets folder.", MessageType.Info);

            if (GUILayout.Button("Scan Entire Project Sizes", GUILayout.Height(28)))
                ProcessScan(false);

            DrawAssetTable(_sizeExplorerResults, false);
        }

        private void DrawActionButtonGroup()
        {
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !_isScanning;
            if (GUILayout.Button("Scan Unused", GUILayout.Height(28))) ProcessScan(true);

           
            GUI.enabled = !_isScanning && _unusedResults.Count > 0;
            if (GUILayout.Button("Select All", GUILayout.Width(80), GUILayout.Height(28))) BulkSelect(true);
            if (GUILayout.Button("None", GUILayout.Width(45), GUILayout.Height(28))) BulkSelect(false);

          
            int count = _unusedResults.Count(r => r.Checked);
            GUI.enabled = !_isScanning && count > 0;

            GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
            if (GUILayout.Button($"Move Selected ({count})", GUILayout.Height(28))) ExecuteMove();

            GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
            if (GUILayout.Button($"Delete Selected ({count})", GUILayout.Height(28))) ExecuteDelete();

            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAssetTable(List<AssetEntry> list, bool isUnusedTab)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            _searchFilter = EditorGUILayout.TextField("Filter Name:", _searchFilter);
            if (isUnusedTab) _showSelectedOnly = GUILayout.Toggle(_showSelectedOnly, "Selected only", GUILayout.Width(110));
            EditorGUILayout.EndHorizontal();

            var filtered = list
                .Where(r => string.IsNullOrEmpty(_searchFilter) || r.Path.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(r => !isUnusedTab || !_showSelectedOnly || r.Checked).ToList();

            // Table Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (isUnusedTab) GUILayout.Label("+", EditorStyles.toolbarButton, GUILayout.Width(24));
            GUILayout.Label("Type", EditorStyles.toolbarButton, GUILayout.Width(44));
            GUILayout.Label("Path", EditorStyles.toolbarButton, GUILayout.MinWidth(200));

            if (GUILayout.Button($"Size {(_sortDescending ? "▼" : "▲")}", EditorStyles.toolbarButton, GUILayout.Width(85)))
            {
                _sortDescending = !_sortDescending;
                list.Sort((a, b) => _sortDescending ? b.SizeBytes.CompareTo(a.SizeBytes) : a.SizeBytes.CompareTo(b.SizeBytes));
            }
            GUILayout.Label("Ping", EditorStyles.toolbarButton, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();

            // Table Content
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            for (int i = 0; i < filtered.Count; i++)
            {
                var e = filtered[i];
                EditorGUILayout.BeginHorizontal(i % 2 == 0 ? _rowEvenStyle : _rowOddStyle);

                if (isUnusedTab) e.Checked = GUILayout.Toggle(e.Checked, "", GUILayout.Width(24));

                DrawTypeTag(e.Extension);

                if (GUILayout.Button(e.Path, EditorStyles.label, GUILayout.MinWidth(200)))
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(e.Path);

                GUILayout.Label(FormatBytes(e.SizeBytes), GUILayout.Width(80));

                if (GUILayout.Button(">", GUILayout.Width(30)))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(e.Path));

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
          
        private void ProcessScan(bool checkReferences)
        {
            _isScanning = true;
            EditorUtility.DisplayProgressBar("Project File Manager", "Searching files...", 0);

            try
            {
                if (checkReferences)
                {
                    _unusedResults.Clear();
                    var extensions = new HashSet<string>(_extensionInput.Split(',').Select(s => s.Trim().ToLower()).Select(s => s.StartsWith(".") ? s : "." + s));
                    _unusedResults = RunAnalysis(true, extensions, _scanPath);
                    _statusMessage = $"Scan complete: {_unusedResults.Count} unused assets.";
                }
                else
                {
                    _sizeExplorerResults.Clear();
                    _sizeExplorerResults = RunAnalysis(false, null, "Assets");
                    _sizeExplorerResults.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
                    _statusMessage = $"Indexed {_sizeExplorerResults.Count} files.";
                }
            }
            finally
            {
                _isScanning = false;
                EditorUtility.ClearProgressBar();
            }
        }

        private List<AssetEntry> RunAnalysis(bool isUnusedCheck, HashSet<string> extensions, string root)
        {
            var allPaths = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith(root) && !AssetDatabase.IsValidFolder(p))
                .ToList();

            if (isUnusedCheck)
            {      
                allPaths = allPaths.Where(p => !p.Contains("/Editor/") && !p.StartsWith(UnusedFolderPath) && extensions.Contains(Path.GetExtension(p).ToLower())).ToList();

                var referencedGuids = new HashSet<string>();
                var dependencyHolders = AssetDatabase.GetAllAssetPaths()
                    .Where(p => new[] { ".unity", ".prefab", ".mat", ".asset", ".anim", ".controller", ".cs", ".shadergraph", ".vfx" }.Contains(Path.GetExtension(p).ToLower()))
                    .ToList();

                for (int i = 0; i < dependencyHolders.Count; i++)
                {
                    if (i % 50 == 0) EditorUtility.DisplayProgressBar("Asset Manager", "Checking references...", (float)i / dependencyHolders.Count);
                    ExtractGuidsFromFile(dependencyHolders[i], referencedGuids);
                }

                return allPaths.Select(p => new AssetEntry(p)).Where(e => !referencedGuids.Contains(e.Guid)).ToList();
            }

            return allPaths.Select(p => new AssetEntry(p)).ToList();
        }
        private void ExecuteMove()
        {
            var targets = _unusedResults.Where(r => r.Checked).ToList();
            string subName = string.IsNullOrEmpty(_targetFolderName) ? "ManualMove" : _targetFolderName;
            string fullDestPath = $"{UnusedFolderPath}/{subName}";

            if (!AssetDatabase.IsValidFolder(fullDestPath))
            {
                if (!AssetDatabase.IsValidFolder(UnusedFolderPath)) AssetDatabase.CreateFolder("Assets", "Unused");
                AssetDatabase.CreateFolder(UnusedFolderPath, subName);
            }

            AssetDatabase.StartAssetEditing();
            foreach (var asset in targets)
                AssetDatabase.MoveAsset(asset.Path, $"{fullDestPath}/{Path.GetFileName(asset.Path)}");
            AssetDatabase.StopAssetEditing();

            AssetDatabase.Refresh();
            ProcessScan(true);
        }

        private void ExecuteDelete()
        {
            var targets = _unusedResults.Where(r => r.Checked).ToList();
            if (!EditorUtility.DisplayDialog("Confirm Delete", $"Delete {targets.Count} assets permanently?", "Delete", "Cancel")) return;

            AssetDatabase.StartAssetEditing();
            foreach (var asset in targets) AssetDatabase.DeleteAsset(asset.Path);
            AssetDatabase.StopAssetEditing();

            AssetDatabase.Refresh();
            ProcessScan(true);
        }

        private void BulkSelect(bool state) => _unusedResults.ForEach(r => r.Checked = state);

        private void DrawPathField(string label, ref string pathValue, string title, bool isSubfolder)
        {
            EditorGUILayout.BeginHorizontal();
            pathValue = EditorGUILayout.TextField(label, pathValue);
            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                string startPath = isSubfolder ? UnusedFolderPath : pathValue;
                string selected = EditorUtility.OpenFolderPanel(title, startPath, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    if (isSubfolder) pathValue = Path.GetFileName(selected);
                    else pathValue = "Assets" + selected.Replace(Application.dataPath, "").Replace('\\', '/');
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTypeTag(string ext)
        {
            string label = ext switch
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

            GUI.backgroundColor = ext switch
            {
                ".png" or ".jpg" or ".tga" => new Color(0.4f, 0.8f, 1.0f),
                ".exr" or ".hdr" => new Color(1.0f, 0.9f, 0.2f),
                ".cubemap" => new Color(0.7f, 0.5f, 1.0f),
                ".mat" => new Color(0.6f, 1.0f, 0.6f),
                ".shader" or ".shadergraph" => new Color(1.0f, 0.85f, 0.4f),
                ".fbx" or ".obj" => new Color(1.0f, 0.65f, 0.4f),
                ".prefab" => new Color(0.6f, 0.9f, 1.0f),
                ".vfx" or ".vfxgraph" => new Color(0.4f, 1.0f, 0.9f),
                ".cs" => new Color(1.0f, 0.75f, 0.75f),
                _ => new Color(0.75f, 0.75f, 0.75f)
            };

            GUILayout.Label(label, _tagStyle, GUILayout.Width(44));
            GUI.backgroundColor = Color.white;
        }

        private void ExtractGuidsFromFile(string path, HashSet<string> targetSet)
        {
            try
            {
                string content = File.ReadAllText(path);
                int index = 0; const string key = "guid: ";
                while ((index = content.IndexOf(key, index, StringComparison.Ordinal)) >= 0)
                {
                    index += key.Length;
                    if (index + 32 <= content.Length) targetSet.Add(content.Substring(index, 32));
                }
            }
            catch { /* Ignore inaccessible files */ }
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(_statusMessage, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;
            _rowEvenStyle = new GUIStyle { normal = { background = CreateColorTex(new Color(0.22f, 0.22f, 0.22f, 0.45f)) }, padding = new RectOffset(2, 2, 2, 2) };
            _rowOddStyle = new GUIStyle { normal = { background = CreateColorTex(new Color(0.17f, 0.17f, 0.17f, 0.45f)) }, padding = new RectOffset(2, 2, 2, 2) };
            _tagStyle = new GUIStyle(EditorStyles.miniButtonMid) { fontSize = 9, alignment = TextAnchor.MiddleCenter };
        }

        private Texture2D CreateColorTex(Color col)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }

        private string FormatBytes(long b) => b < 1048576 ? $"{b / 1024f:F1} KB" : $"{b / 1048576f:F1} MB";
    }
}