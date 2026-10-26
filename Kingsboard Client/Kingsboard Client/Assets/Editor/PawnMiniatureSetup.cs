using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kingsboard.Pawns.EditorTools
{
    /// <summary>
    /// One-off editor step that swaps the Pawn prefab's capsule body for the miniature model in
    /// Assets/Models/Pawns/PawnMiniature.obj and gives it a dark base. Safe to run more than once.
    /// </summary>
    public static class PawnMiniatureSetup
    {
        private const string ModelPath = "Assets/Models/Pawns/PawnMiniature.obj";
        private const string PrefabPath = "Assets/Prefabs/Pawns/Pawn.prefab";
        private const string BaseMaterialPath = "Assets/Materials/Pawns/PawnBase.mat";

        /// <summary>
        /// Configures the model's import, creates the base material and updates the Pawn prefab.
        /// </summary>
        [MenuItem("Kingsboard/Set Up Pawn Miniature")]
        public static void Run()
        {
            // Import as plain meshes: our own materials are assigned on the prefab.
            ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.globalScale = 1f;
            importer.useFileScale = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.SaveAndReimport();

            Mesh[] meshes = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Mesh>().ToArray();
            Mesh figure = meshes.FirstOrDefault(m => m.name.Contains("PawnFigure"));
            Mesh plinth = meshes.FirstOrDefault(m => m.name.Contains("PawnBase"));
            if (figure == null || plinth == null)
            {
                Debug.LogError($"[PawnMiniatureSetup] Meshes not found in {ModelPath}: {string.Join(", ", meshes.Select(m => m.name))}");
                return;
            }

            Material baseMaterial = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath);
            if (baseMaterial == null)
            {
                baseMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                baseMaterial.color = new Color(0.12f, 0.12f, 0.13f);
                baseMaterial.SetFloat("_Smoothness", 0.35f);
                AssetDatabase.CreateAsset(baseMaterial, BaseMaterialPath);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform body = root.transform.Find("Body");
                body.localPosition = Vector3.zero;
                body.localRotation = Quaternion.identity;
                body.localScale = Vector3.one;
                body.GetComponent<MeshFilter>().sharedMesh = figure;

                // Clicks select the pawn through this collider, so it wraps the whole figure.
                CapsuleCollider collider = body.GetComponent<CapsuleCollider>();
                collider.center = new Vector3(0f, 0.36f, 0f);
                collider.radius = 0.17f;
                collider.height = 0.72f;
                collider.direction = 1;

                Transform plinthTransform = root.transform.Find("Base");
                if (plinthTransform == null)
                {
                    plinthTransform = new GameObject("Base", typeof(MeshFilter), typeof(MeshRenderer)).transform;
                    plinthTransform.SetParent(root.transform, false);
                    plinthTransform.SetSiblingIndex(body.GetSiblingIndex() + 1);
                }
                plinthTransform.localPosition = Vector3.zero;
                plinthTransform.localRotation = Quaternion.identity;
                plinthTransform.localScale = Vector3.one;
                plinthTransform.GetComponent<MeshFilter>().sharedMesh = plinth;
                plinthTransform.GetComponent<MeshRenderer>().sharedMaterial = baseMaterial;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[PawnMiniatureSetup] Done. Figure bounds {figure.bounds}, base bounds {plinth.bounds}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
