using Unity.Collections;
using Unity.Mathematics;

#if UNITY_EDITOR
#endif

namespace Moths.Terrain.Modifiers
{
    public struct UnmanagedTerrainData
    {
        public NativeArray<float> Heightmap;
        public NativeArray<float> Alphamaps;

        public float3 Position;
        public float3 Size;
        public int HeightmapResolution;
        public int AlphamapLayers;
        public int AlphamapWidth;
        public int AlphamapHeight;
        public int TreePrototypes;

        public void SetAlphamapLayerWeight(int alphamapIndex, int layerIndex, float weight)
        {
            weight = math.saturate(weight);
            float leftover = 1f - weight;

            if (AlphamapIndexToLayerIndex(alphamapIndex) == layerIndex)
            {
                Alphamaps[alphamapIndex] = weight;
            }
            else
            {
                Alphamaps[alphamapIndex] *= leftover;
            }
        }

        public int AlphamapIndexToLayerIndex(int index)
        {
            return index % AlphamapLayers;
        }

        public int AlphamapIndexToHeightmapIndex(int index)
        {
            int pixelIndex = index / AlphamapLayers;

            int x = pixelIndex % AlphamapWidth;
            int y = pixelIndex / AlphamapWidth;

            int hx = (int)math.round(
                (float)x / (AlphamapWidth - 1) * (HeightmapResolution - 1));

            int hy = (int)math.round(
                (float)y / (AlphamapHeight - 1) * (HeightmapResolution - 1));

            return hy * HeightmapResolution + hx;
        }

        public float3 GetWorldPositionAtHeightmapIndex(int index, NativeArray<float> heightMap = default)
        {
            int y = index / HeightmapResolution;
            int x = index % HeightmapResolution;

            float2 uv = new float2(x, y) / (HeightmapResolution);

            float worldX = Position.x + uv.x * Size.x;
            float worldZ = Position.z + uv.y * Size.z;
            float worldY = heightMap.IsCreated ? Position.y + Size.y * heightMap[index] : Position.y;

            return new float3(worldX, worldY, worldZ);
        }

        public float3 WorldToLocationPosition(float3 worldPosition)
        {
            return (worldPosition - Position) / Size;
        }
    }
}