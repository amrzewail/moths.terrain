namespace Moths.Terrain.Blending
{
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
                _terrain = GetClosestCurrentTerrain(_renderer.bounds.center);
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
            Terrain[] terrains = Terrain.activeTerrains;

            if (terrains == null || terrains.Length == 0)
                return null;

            Terrain closest = terrains[0];
            float closestSqrDist = float.PositiveInfinity;

            foreach (var terrain in terrains)
            {
                // Build terrain bounds
                Vector3 terrainPos = terrain.GetPosition();
                Vector3 size = terrain.terrainData.size;

                Bounds bounds = new Bounds(
                    terrainPos + size * 0.5f, // center
                    size                       // size
                );

                // Closest point on bounds to the position
                Vector3 closestPoint = bounds.ClosestPoint(position);
                float sqrDist = (closestPoint - position).sqrMagnitude;

                if (sqrDist < closestSqrDist)
                {
                    closestSqrDist = sqrDist;
                    closest = terrain;
                }
            }

            return closest;
        }
    }
}