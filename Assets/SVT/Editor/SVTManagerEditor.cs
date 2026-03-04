using UnityEngine;
using UnityEditor;
using SVT.Core;

namespace SVT.Editor
{
    /// <summary>
    /// Custom Inspector for SVTManager.
    /// SVTManager的自定义Inspector。
    /// </summary>
    [CustomEditor(typeof(SVTManager))]
    public class SVTManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SVTManager mgr = (SVTManager)target;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Runtime Actions", EditorStyles.boldLabel);

            if (GUILayout.Button("Rebuild Quadtree"))
                mgr.RebuildQuadTree();

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("Open SVT Editor Window"))
                SVTEditorWindow.ShowWindow();
        }
    }
}
