
namespace Moths.Terrain.Blending.Editor
{
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    [CustomEditor(typeof(TerrainBlender))]
    public class TerrainBlenderInspector : Editor
    {
        private TerrainBlender _target => (TerrainBlender)target;

        private VisualElement _root;
        private Button _updateButton;
        private Label _samplingLabel;

        public override VisualElement CreateInspectorGUI()
        {

            _root = new VisualElement();

            _updateButton = _updateButton = new Button(UpdateCallback)
            {
                text = "Update",
            };

            _samplingLabel = new Label("Sampling...");

            _root.Add(_updateButton);

            return _root;
        }

        private void OnEnable()
        {
            EditorApplication.update += Update;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Update;
        }

        private void Update()
        {
            if (_root == null) return;

            TerrainTextureSampler sampler = default;
            _target.GetSampler(ref sampler);
            if (sampler.IsSampling)
            {
                _samplingLabel.text = $"Sampling: ({(int)(sampler.Progress * 100)} / 100)";
                if (_root[0] == _samplingLabel) return;
                _root.RemoveAt(0);
                _root.Add(_samplingLabel);
            }
            else
            {
                if (_root[0] == _updateButton) return;
                _root.RemoveAt(0);
                _root.Add(_updateButton);
            }

        }

        private async void UpdateCallback()
        {
            if (!_target.Terrain) return;

            TerrainTextureSampler sampler = default;
            _target.GetSampler(ref sampler);

            if (sampler.IsSampling) return;

            Undo.RecordObject(_target, "Terrain blending sampler update");

            TerrainLayer[] layers = _target.Terrain.terrainData.terrainLayers;

            for (int i = 0; i < layers.Length; i++)
            {
                Texture2D texture = layers[i].diffuseTexture;
                if (texture.isReadable) continue;

                string path = UnityEditor.AssetDatabase.GetAssetPath(texture);

                UnityEditor.TextureImporter importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
                if (importer != null && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    Debug.Log($"Enabled Read/Write for texture: {path}");
                }
            }

            UnityEditor.AssetDatabase.DeleteAsset(UnityEditor.AssetDatabase.GetAssetPath(sampler.control));

            sampler.Sample(_target.Terrain);

            var sceneFolder = Path.GetDirectoryName(_target.gameObject.scene.path);
            string sceneName = Path.GetFileNameWithoutExtension(_target.gameObject.scene.name);
            // Create subfolder with the scene's name
            string targetFolder = Path.Combine(sceneFolder, sceneName);
            string assetPath = Path.Combine(targetFolder, Guid.NewGuid().ToString() + "_ControlMap.png");

            byte[] pngData = sampler.control.EncodeToPNG();

            // Write to file
            await File.WriteAllBytesAsync(assetPath, pngData);

            UnityEditor.AssetDatabase.ImportAsset(assetPath);

            // Set texture settings (optional)
            UnityEditor.TextureImporter importer2 = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(assetPath);
            if (importer2 != null)
            {
                importer2.textureType = UnityEditor.TextureImporterType.Default;
                importer2.alphaSource = UnityEditor.TextureImporterAlphaSource.FromInput;
                importer2.isReadable = true;
                importer2.SaveAndReimport();
            }

            // Log and highlight the new asset
            Texture2D savedTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

            sampler.control = savedTex;
        }
    }
}