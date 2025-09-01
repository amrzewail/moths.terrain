using UnityEngine;

namespace Moths.Terrain.Blending
{
    using System;
    using System.IO;
    using UnityEngine;


    [ExecuteInEditMode]
    public class TerrainBlendable : MonoBehaviour
    {
        [SerializeField] Renderer _renderer;
        [SerializeField] float _blendRange = 2;

        private Terrain _terrain;

        private MaterialPropertyBlock _propertyBlock;

        private void Reset()
        {
            _renderer = GetComponentInChildren<Renderer>();
        }

        private void Update()
        {
            if (!_renderer) return;

            if (!Application.isPlaying || !_terrain)
            {
                _terrain = GetClosestCurrentTerrain(transform.position);
            }

            if (!_terrain) return;

            TerrainTextureSampler sampler = TerrainTextureSampler.GetSampler(_terrain);

            if (sampler == null) return;

            if (!sampler.control) return;

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            _propertyBlock.SetTexture("_controlMap", sampler.control);

            if (sampler.diffuseLayers[0])
            {
                _propertyBlock.SetTexture("_layer0", sampler.diffuseLayers[0]);
            }

            if (sampler.diffuseLayers[1])
            {
                _propertyBlock.SetTexture("_layer1", sampler.diffuseLayers[1]);
            }

            if (sampler.diffuseLayers[2])
            {
                _propertyBlock.SetTexture("_layer2", sampler.diffuseLayers[2]);
            }

            if (sampler.normalLayers[0])
            {
                _propertyBlock.SetTexture("_normal0", sampler.normalLayers[0]);
            }

            if (sampler.normalLayers[1])
            {
                _propertyBlock.SetTexture("_normal1", sampler.normalLayers[1]);
            }

            if (sampler.normalLayers[2])
            {
                _propertyBlock.SetTexture("_normal2", sampler.normalLayers[2]);
            }

            _propertyBlock.SetVector("_size0", sampler.tileSizes[0]);
            _propertyBlock.SetVector("_size1", sampler.tileSizes[1]);
            _propertyBlock.SetVector("_size2", sampler.tileSizes[2]);

            _propertyBlock.SetVector("_terrainSize", sampler.terrainSize);
            _propertyBlock.SetVector("_terrainPositionWS", _terrain.transform.position);
            _propertyBlock.SetFloat("_blendRange", _blendRange);

            _renderer.SetPropertyBlock(_propertyBlock);
        }

        Terrain GetClosestCurrentTerrain(Vector3 position)
        {
            //Get all terrain
            Terrain[] terrains = Terrain.activeTerrains;

            //Make sure that terrains length is ok
            if (terrains.Length == 0)
                return null;

            //If just one, return that one terrain
            if (terrains.Length == 1)
                return terrains[0];

            //Get the closest one to the player
            float lowDist = (terrains[0].GetPosition() - position).sqrMagnitude;
            var terrainIndex = 0;

            for (int i = 1; i < terrains.Length; i++)
            {
                Terrain terrain = terrains[i];
                Vector3 terrainPos = terrain.GetPosition();

                //Find the distance and check if it is lower than the last one then store it
                var dist = (terrainPos - position).sqrMagnitude;
                if (dist < lowDist)
                {
                    lowDist = dist;
                    terrainIndex = i;
                }
            }
            return terrains[terrainIndex];
        }
    }
}