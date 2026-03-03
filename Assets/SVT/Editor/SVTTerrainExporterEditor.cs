using UnityEngine;
using UnityEditor;
using SVT.Terrain;

namespace SVT.Editor
{
    /// <summary>
    /// Custom Inspector for SVTTerrainExporter.
    /// SVTTerrainExporter的自定义Inspector。
    /// </summary>
    [CustomEditor(typeof(SVTTerrainExporter))]
    public class SVTTerrainExporterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SVTTerrainExporter exporter = (SVTTerrainExporter)target;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Export Actions", EditorStyles.boldLabel);

            if (GUILayout.Button("Export Quadtree Mesh Patches"))
            {
                if (EditorUtility.DisplayDialog("SVT Export",
                    "Replace all existing exported patch GameObjects?", "Export", "Cancel"))
                {
                    exporter.Export();
                    EditorUtility.SetDirty(exporter.gameObject);
                }
            }

            EditorGUI.BeginDisabledGroup(!Application.isPlaying);
            if (GUILayout.Button("Clear Exported Patches"))
                exporter.ClearExportedPatches();
            EditorGUI.EndDisabledGroup();
        }
    }
}
