using UnityEngine;
using Moths.Serialization;
using Unity.Collections;
using System.Linq;
using Unity.Mathematics;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Moths.Terrain.Modifiers
{
    using Terrain = UnityEngine.Terrain;

    [RequireComponent(typeof(Terrain))]
    [ExecuteInEditMode]
    public class TerrainModifiers : MonoBehaviour
    {
        private Terrain Terrain => _terrain ? _terrain : (_terrain = GetComponent<Terrain>());
        private TerrainCollider TerrainCollider => _terrainCollider ? _terrainCollider : (_terrainCollider = GetComponent<TerrainCollider>());

        [SerializeField] bool _bakeOnStart = false;
        [SerializeField] TerrainData _sourceTerrainData;
        [SerializeField, Attributes.ReadOnly] TerrainData _runtimeTerrainData;


        [SerializeField] InterfaceComponent<ITerrainModifier>[] _modifiers;

        private TerrainData _modifiedTerrainData;

        private Terrain _terrain;
        private Terrain _proxyTerrain;
        private TerrainCollider _terrainCollider;
        private bool _isProxy;
        private CancellationTokenSource _cts; 

        private int _updatedModifiersCount;
        private float[,] _heights;
        private float[,,] _alphamaps;
        private TreeInstance[] _treeInstances;

        private float[,] _heightSliceBuffer;
        private float[,] _heightEdgeSliceBuffer;
        private float[,,] _alphamapSliceBuffer;

        private NativeArray<float> _nativeSourceHeightmap;
        private NativeArray<float> _nativeSourceAlphamaps;
        private NativeList<TreeInstance> _nativeSourceTreeInstances;

        private NativeArray<float> _nativeModifiedHeightmap;
        private NativeArray<float> _nativeModifiedAlphamaps;
        private NativeList<TreeInstance> _nativeModifiedTreeInstances;

        private void Reset()
        {
            _sourceTerrainData = Terrain.terrainData;
        }

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                SetTerrainData(_sourceTerrainData);
            }
            else
            {
                SetTerrainData(_runtimeTerrainData);
            }
        }

        private void Start()
        {
            if (Application.isPlaying)
            {
#if UNITY_EDITOR
                if (_bakeOnStart)
                {
                    BakeRuntimeData();
                }
#endif
                SetTerrainData(_runtimeTerrainData);
            }
        }

        private void OnDestroy()
        {
            if (!Application.isPlaying)
            {
                DisposeData();
            }
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                DisposeData();
                DestroyProxyTerrain();
                SetTerrainData(_sourceTerrainData);
            }
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                if (!_isProxy && Selection.activeGameObject && Selection.activeGameObject.GetComponent<Terrain>())
                {
                    SetTerrainData(_sourceTerrainData);
                    UpdateModifications();
                }
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Bake Runtime Data")]
        public void BakeRuntimeData()
        {
            if (Application.isPlaying || _isProxy || GetActiveModifiersCount() == 0) return;

            string sceneFolder = Path.GetDirectoryName(gameObject.scene.path);
            if (string.IsNullOrEmpty(sceneFolder))
            {
                Debug.LogError("Save the scene before baking terrain data.", this);
                return;
            }

            UpdateModifications();
            ApplyModifiedHeightmap();
            ApplyModifiedAlphamaps();
            ApplyModifiedTrees();

            string folder = $"{sceneFolder}/{gameObject.scene.name}";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(sceneFolder, gameObject.scene.name);

            string sourcePath = AssetDatabase.GetAssetPath(_sourceTerrainData);
            string assetPath = $"{folder}/{_sourceTerrainData.name}_Modified.asset";

            // Overwrite cleanly if an old bake exists
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);

            AssetDatabase.CopyAsset(sourcePath, assetPath);
            var bakedData = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
            bakedData.name = $"{_sourceTerrainData.name}_Modified";

            // Write directly to the on-disk asset so SplatAlpha sub-assets are serialized
            bakedData.SetHeightsDelayLOD(0, 0, _heights);
            bakedData.SyncHeightmap();
            bakedData.SetAlphamaps(0, 0, _alphamaps);
            bakedData.SetTreeInstances(_treeInstances, true);

            EditorUtility.SetDirty(bakedData);
            AssetDatabase.SaveAssets();

            _runtimeTerrainData = bakedData;
            EditorUtility.SetDirty(this); // Ensures the scene saves the new _runtimeTerrainData reference
        }
#endif

        private void DisposeData()
        {
            _heights = null;
            _alphamaps = null;
            _treeInstances = null;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (_nativeSourceHeightmap.IsCreated) _nativeSourceHeightmap.Dispose();
            if (_nativeSourceAlphamaps.IsCreated) _nativeSourceAlphamaps.Dispose();

            if (_nativeModifiedHeightmap.IsCreated) _nativeModifiedHeightmap.Dispose();
            if (_nativeModifiedAlphamaps.IsCreated) _nativeModifiedAlphamaps.Dispose();

            if (_nativeSourceTreeInstances.IsCreated) _nativeSourceTreeInstances.Dispose();
            if (_nativeModifiedTreeInstances.IsCreated) _nativeModifiedTreeInstances.Dispose();

            if (_modifiedTerrainData)
            {
                DestroyImmediate(_modifiedTerrainData);
                _modifiedTerrainData = null;
            }

            TerrainCallbacks.heightmapChanged -= OnHeightmapChanged;
            TerrainCallbacks.textureChanged -= OnTextureChanged;
        }


#if UNITY_EDITOR
        private void Update()
        {
            if (Application.isPlaying) return;
            if (!_sourceTerrainData) return;

            if (GetActiveModifiersCount() == 0)
            {
                DisposeData();
                DestroyProxyTerrain();
                SetTerrainData(_sourceTerrainData);

                return;
            }

            if (!_isProxy && Selection.activeGameObject && Selection.activeGameObject.GetComponent<Terrain>())
            {
                SetTerrainData(_sourceTerrainData);
                CreateProxyTerrain();
            }
            else
            {
                DestroyProxyTerrain();
                if (Terrain.terrainData == _sourceTerrainData)
                {
                    UpdateAllModificationsAndApplySliced();
                    SetTerrainData(_modifiedTerrainData);
                }
                else if (ShouldUpdateModifications())
                {
                    UpdateAllModificationsAndApplySliced();
                }
            }
        }
#endif

#if UNITY_EDITOR

        private void CreateProxyTerrain()
        {
            if (Application.isPlaying) return;

            if (!_proxyTerrain)
            {
                if (!_modifiedTerrainData)
                {
                    UpdateModifications();
                }

                _proxyTerrain = GameObject.Instantiate(this, this.transform).GetComponent<Terrain>();
                _proxyTerrain.gameObject.name = $"{this.name} (Proxy)";
                _proxyTerrain.transform.localPosition = Vector3.zero;
                _proxyTerrain.GetComponent<TerrainModifiers>().DisposeData();
                _proxyTerrain.GetComponent<TerrainModifiers>()._isProxy = true;
                _proxyTerrain.GetComponent<TerrainCollider>().enabled = false;

                _proxyTerrain.gameObject.hideFlags = HideFlags.HideAndDontSave;
                _proxyTerrain.drawTreesAndFoliage = false;

                Terrain.drawHeightmap = false;

                DisposeData();

                TerrainCallbacks.heightmapChanged += OnHeightmapChanged;
                TerrainCallbacks.textureChanged += OnTextureChanged;
            }
        }

        private void DestroyProxyTerrain()
        {
            if (Application.isPlaying) return;

            if (_proxyTerrain)
            {
                DestroyImmediate(_proxyTerrain.gameObject);
            }

            Terrain.drawHeightmap = true;

            TerrainCallbacks.heightmapChanged -= OnHeightmapChanged;
            TerrainCallbacks.textureChanged -= OnTextureChanged;
        }
#endif


        private void OnTextureChanged(Terrain terrain, string textureName, RectInt texelRegion, bool synched)
        {
            if (terrain != Terrain) return;
        
            if (_proxyTerrain)
            {
                _proxyTerrain.GetComponent<TerrainModifiers>().UpdateAlphamaps(texelRegion);
                _proxyTerrain.GetComponent<TerrainModifiers>().UpdateModifications();
                _proxyTerrain.GetComponent<TerrainModifiers>().ApplyModifiedAlphamaps(alphamapsRegion: texelRegion);
            }
        }

        private void OnHeightmapChanged(Terrain terrain, RectInt heightRegion, bool synched)
        {
            if (terrain != Terrain) return;

            if (_proxyTerrain)
            {
                _proxyTerrain.GetComponent<TerrainModifiers>().UpdateHeightmap(heightRegion);
                _proxyTerrain.GetComponent<TerrainModifiers>().UpdateModifications();
                _proxyTerrain.GetComponent<TerrainModifiers>().ApplyModifiedHeightmap(heightRegion);
            }
        }

        private async void UpdateAllModificationsAndApplySliced(bool applyHeightmap = true, bool applyAlphamaps = true, bool applyTrees = true)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            UpdateModifications();
            try { await Task.Delay(200, token); } catch (TaskCanceledException) { return; }
            try { await Task.Delay(50, token); } catch (TaskCanceledException) { return; }

            const int FRAME_DELAY_MS = 100;

            if (applyHeightmap)
            {
                ApplyModifiedHeightmap();
                try { await Task.Delay(FRAME_DELAY_MS, token); } catch (TaskCanceledException) { return; }
            }

            if (applyAlphamaps)
            {
                ApplyModifiedAlphamaps();
                try { await Task.Delay(FRAME_DELAY_MS, token); } catch (TaskCanceledException) { return; }
            }

            if (applyTrees)
            {
                ApplyModifiedTrees();
            }
        }

        private void UpdateModifications()
        {
            if (!_modifiedTerrainData)
            {
                _modifiedTerrainData = Instantiate(_sourceTerrainData);
            }

            _updatedModifiersCount = 0;

            if (_modifiers == null) return;

            int index = 0;
            int heightmapResolution = _modifiedTerrainData.heightmapResolution;
            int alphamapWidth = _modifiedTerrainData.alphamapWidth;
            int alphamapHeight = _modifiedTerrainData.alphamapHeight;
            int alphamapLayers = _modifiedTerrainData.alphamapLayers;

            TryInitializeData();

            index = 0;

            int heightsLength = _nativeSourceHeightmap.Length;
            int alphamapsLength = _nativeSourceAlphamaps.Length;

            _nativeModifiedHeightmap.CopyFrom(_nativeSourceHeightmap);
            _nativeModifiedAlphamaps.CopyFrom(_nativeSourceAlphamaps);
            _nativeModifiedTreeInstances.Clear();
            _nativeModifiedTreeInstances.CopyFrom(_nativeSourceTreeInstances);

            UnmanagedTerrainData data = default;
            data.Heightmap = _nativeModifiedHeightmap;
            data.Alphamaps = _nativeModifiedAlphamaps;
            data.Position = Terrain.transform.position;
            data.Size = _sourceTerrainData.size;
            data.HeightmapResolution = heightmapResolution;
            data.AlphamapLayers = alphamapLayers;
            data.AlphamapWidth = alphamapWidth;
            data.AlphamapHeight = alphamapHeight;
            data.TreePrototypes = _sourceTerrainData.treePrototypes.Length;

            _updatedModifiersCount = 0;
            foreach (var modifier in _modifiers)
            {
                if (!modifier) continue;

                if (modifier.Value.IsActive)
                {
                    modifier.Value.ApplyModifier(data, _nativeModifiedTreeInstances);
                    _updatedModifiersCount++;
                }
            }
        }

        private void ApplyModifiedHeightmap(RectInt heightmapRegion = default, bool syncHeightmap = true)
        {
            if (!_modifiedTerrainData) return;
            if (!_nativeModifiedHeightmap.IsCreated) return;

            int heightmapResolution = _modifiedTerrainData.heightmapResolution;

            if (heightmapRegion == default)
            {
                int index = 0;
                for (int y = 0; y < heightmapResolution; y++)
                {
                    for (int x = 0; x < heightmapResolution; x++)
                    {
                        _heights[y, x] = _nativeModifiedHeightmap[index++];
                    }
                }

                _modifiedTerrainData.SetHeightsDelayLOD(0, 0, _heights);
                if (syncHeightmap) _modifiedTerrainData.SyncHeightmap();
                return;
            }

            if (heightmapRegion.xMax > heightmapResolution || heightmapRegion.yMax > heightmapResolution) return;

            int regionWidth = heightmapRegion.width;
            int regionHeight = heightmapRegion.height;

            // Pick standard interior buffer vs. 1-texel-wider/taller edge buffer (due to 2^n + 1 resolution)
            float[,] buffer;
            if (_heightSliceBuffer != null && _heightSliceBuffer.GetLength(0) == regionHeight && _heightSliceBuffer.GetLength(1) == regionWidth)
            {
                buffer = _heightSliceBuffer;
            }
            else if (_heightEdgeSliceBuffer != null && _heightEdgeSliceBuffer.GetLength(0) == regionHeight && _heightEdgeSliceBuffer.GetLength(1) == regionWidth)
            {
                buffer = _heightEdgeSliceBuffer;
            }
            else
            {
                buffer = new float[regionHeight, regionWidth];
                if (_heightSliceBuffer == null) _heightSliceBuffer = buffer;
                else _heightEdgeSliceBuffer = buffer;
            }

            for (int y = 0; y < regionHeight; y++)
            {
                int srcIndex = (heightmapRegion.y + y) * heightmapResolution + heightmapRegion.x;

                for (int x = 0; x < regionWidth; x++)
                {
                    buffer[y, x] = _nativeModifiedHeightmap[srcIndex++];
                }
            }

            _modifiedTerrainData.SetHeightsDelayLOD(heightmapRegion.x, heightmapRegion.y, buffer);

            if (syncHeightmap)
            {
                _modifiedTerrainData.SyncHeightmap();
            }
        }

        private void ApplyModifiedAlphamaps(RectInt alphamapsRegion = default)
        {
            if (!_modifiedTerrainData) return;
            if (!_nativeModifiedAlphamaps.IsCreated) return;

            int alphamapWidth = _modifiedTerrainData.alphamapWidth;
            int alphamapHeight = _modifiedTerrainData.alphamapHeight;
            int alphamapLayers = _modifiedTerrainData.alphamapLayers;

            if (alphamapsRegion == default)
            {
                int index = 0;
                for (int y = 0; y < alphamapHeight; y++)
                {
                    for (int x = 0; x < alphamapWidth; x++)
                    {
                        for (int t = 0; t < alphamapLayers; t++)
                        {
                            _alphamaps[y, x, t] = _nativeModifiedAlphamaps[index++];
                        }
                    }
                }

                _modifiedTerrainData.SetAlphamaps(0, 0, _alphamaps);
                return;
            }

            if (alphamapsRegion.xMax > alphamapWidth || alphamapsRegion.yMax > alphamapHeight) return;

            int regionWidth = alphamapsRegion.width;
            int regionHeight = alphamapsRegion.height;

            if (_alphamapSliceBuffer == null || _alphamapSliceBuffer.GetLength(0) != regionHeight || _alphamapSliceBuffer.GetLength(1) != regionWidth || _alphamapSliceBuffer.GetLength(2) != alphamapLayers)
            {
                _alphamapSliceBuffer = new float[regionHeight, regionWidth, alphamapLayers];
            }

            for (int y = 0; y < regionHeight; y++)
            {
                int srcIndex = ((alphamapsRegion.y + y) * alphamapWidth + alphamapsRegion.x) * alphamapLayers;

                for (int x = 0; x < regionWidth; x++)
                {
                    for (int t = 0; t < alphamapLayers; t++)
                    {
                        _alphamapSliceBuffer[y, x, t] = _nativeModifiedAlphamaps[srcIndex++];
                    }
                }
            }

            _modifiedTerrainData.SetAlphamaps(alphamapsRegion.x, alphamapsRegion.y, _alphamapSliceBuffer);
        }

        private void ApplyModifiedTrees()
        {
            if (!_modifiedTerrainData) return;
            if (!_nativeModifiedTreeInstances.IsCreated) return;

            Array.Resize(ref _treeInstances, _nativeModifiedTreeInstances.Length);
            for (int i = 0; i < _treeInstances.Length; i++)
            {
                _treeInstances[i] = _nativeModifiedTreeInstances[i];
            }

            _modifiedTerrainData.SetTreeInstances(_treeInstances, true);
        }

        private void UpdateHeightmap(RectInt rect)
        {
            TryInitializeData();
            float[,] updatedHeights = _sourceTerrainData.GetHeights(rect.x, rect.y, rect.width, rect.height);

            int height = updatedHeights.GetLength(0);
            int width = updatedHeights.GetLength(1);

            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    int index = (rect.y + y) * _sourceTerrainData.heightmapResolution + (rect.x + x);
                    _nativeSourceHeightmap[index] = updatedHeights[y, x];
                }
            }
        }

        private void UpdateAlphamaps(RectInt rect)
        {
            TryInitializeData();

            float[,,] updatedAlphamaps = _sourceTerrainData.GetAlphamaps(
                rect.x, rect.y, rect.width, rect.height);

            int height = updatedAlphamaps.GetLength(0);
            int width = updatedAlphamaps.GetLength(1);
            int layers = updatedAlphamaps.GetLength(2);

            int alphamapWidth = _sourceTerrainData.alphamapWidth;

            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    int index = ((rect.y + y) * alphamapWidth + (rect.x + x)) * layers;

                    for (int layer = 0; layer < layers; ++layer)
                    {
                        _nativeSourceAlphamaps[index + layer] = updatedAlphamaps[y, x, layer];
                    }
                }
            }
        }

        private void TryInitializeData()
        {
            int index = 0;
            int heightmapResolution = _sourceTerrainData.heightmapResolution;
            int alphamapWidth = _sourceTerrainData.alphamapWidth;
            int alphamapHeight = _sourceTerrainData.alphamapHeight;
            int alphamapLayers = _sourceTerrainData.alphamapLayers;
            int treeInstancesCount = _sourceTerrainData.treeInstanceCount;

            if (_heights == null || _alphamaps == null || _treeInstances == null)
            {
                _heights = _sourceTerrainData.GetHeights(0, 0, _sourceTerrainData.heightmapResolution, _sourceTerrainData.heightmapResolution);
                _alphamaps = _sourceTerrainData.GetAlphamaps(0, 0, _sourceTerrainData.alphamapWidth, _sourceTerrainData.alphamapHeight);
                _treeInstances = _sourceTerrainData.treeInstances;
            }

            if (!_nativeSourceHeightmap.IsCreated)
            {
                _nativeSourceHeightmap = new NativeArray<float>(heightmapResolution * heightmapResolution, Allocator.Persistent);
                index = 0;
                for (int y = 0; y < _sourceTerrainData.heightmapResolution; ++y)
                {
                    for (int x = 0; x < _sourceTerrainData.heightmapResolution; ++x)
                    {
                        _nativeSourceHeightmap[index++] = _heights[y, x];
                    }
                }
            }

            if (!_nativeSourceAlphamaps.IsCreated)
            {
                _nativeSourceAlphamaps = new NativeArray<float>(alphamapWidth * alphamapHeight * alphamapLayers, Allocator.Persistent);
                index = 0;
                for (int y = 0; y < alphamapHeight; ++y)
                {
                    for (int x = 0; x < alphamapWidth; ++x)
                    {
                        for (int t = 0; t < alphamapLayers; ++t)
                        {
                            _nativeSourceAlphamaps[index++] = _alphamaps[y, x, t];
                        }
                    }
                }
            }

            if (!_nativeSourceTreeInstances.IsCreated) 
            {
                _nativeSourceTreeInstances = new NativeList<TreeInstance>(treeInstancesCount, Allocator.Persistent);
                foreach (var instance in _treeInstances) _nativeSourceTreeInstances.Add(instance);
            }

            if (!_nativeModifiedHeightmap.IsCreated || _nativeModifiedHeightmap.Length != heightmapResolution * heightmapResolution)
            {
                if (_nativeModifiedHeightmap.IsCreated)
                    _nativeModifiedHeightmap.Dispose();
                _nativeModifiedHeightmap = new NativeArray<float>(heightmapResolution * heightmapResolution, Allocator.Persistent);
            }

            if (!_nativeModifiedAlphamaps.IsCreated || _nativeModifiedAlphamaps.Length != alphamapWidth * alphamapHeight * alphamapLayers)
            {
                if (_nativeModifiedAlphamaps.IsCreated)
                    _nativeModifiedAlphamaps.Dispose();
                _nativeModifiedAlphamaps = new NativeArray<float>(alphamapWidth * alphamapHeight * alphamapLayers, Allocator.Persistent);
            }

            if (!_nativeModifiedTreeInstances.IsCreated || _nativeModifiedTreeInstances.Length < treeInstancesCount)
            {
                if (_nativeModifiedTreeInstances.IsCreated)
                    _nativeModifiedTreeInstances.Dispose();
                _nativeModifiedTreeInstances = new NativeList<TreeInstance>(treeInstancesCount, Allocator.Persistent);
            }
        }

        private bool ShouldUpdateModifications()
        {
            if (_modifiers == null)
            {
                return _updatedModifiersCount > 0;
            }

            if (_updatedModifiersCount != GetActiveModifiersCount()) return true;

            foreach (var modifier in _modifiers)
            {
                if (!modifier) continue;

                if (modifier.Value.IsApplyRequired())
                {
                    return true;
                }
            }

            return false;
        }

        private int GetActiveModifiersCount()
        {
            if (_modifiers == null) return 0;

            int count = 0;
            for (int i = 0; i < _modifiers.Length; i++)
            {
                if (!_modifiers[i]) continue;

                count += math.select(0, 1, _modifiers[i].Value.IsActive);
            }
            return count;
        }

        private void SetTerrainData(TerrainData data)
        {
            if (!data) return;
            if (Terrain.terrainData == data) return;

            Terrain.terrainData = data;
            TerrainCollider.terrainData = data;
        }
    }
}