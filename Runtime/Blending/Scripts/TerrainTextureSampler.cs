using UnityEngine;

namespace Moths.Terrain.Blending
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Unity.Collections;
    using UnityEngine;

    [System.Serializable]
    public class TerrainTextureSampler
    {
        public Vector3 terrainSize;
        public Texture2D control;
        public Texture2D[] diffuseLayers;
        public Texture2D[] normalLayers;
        public Vector2[] tileSizes;

        public bool IsSampling { get; private set; }

        public float Progress { get; private set; }

        private static Dictionary<Terrain, TerrainTextureSampler> _sampledTerrains = new Dictionary<Terrain, TerrainTextureSampler>();

        public static TerrainTextureSampler GetSampler(Terrain terrain)
        {
            if (_sampledTerrains.ContainsKey(terrain)) return _sampledTerrains[terrain];
            return default;
        }

        public void RegisterSampler(Terrain terrain)
        {
            if (_sampledTerrains.ContainsKey(terrain)) return;
            _sampledTerrains.Add(terrain, this);
        }

        public void Sample(Terrain terrain)
        {
            if (!terrain) return;

            IsSampling = true;

            var data = terrain.terrainData;

            int textureWidth = data.alphamapWidth;
            int textureHeight = data.alphamapHeight;
            int heightMapResolution = data.heightmapResolution;

            control = new Texture2D(textureWidth, textureHeight);



            float[,,] alphaMap = data.GetAlphamaps(0, 0, textureWidth, textureHeight);
            float[,] heights = data.GetHeights(0, 0, heightMapResolution, heightMapResolution);
            NativeArray<Color> controlColors = new NativeArray<Color>(textureWidth * textureHeight, Allocator.Persistent);

            TerrainLayer[] layers = data.terrainLayers;

            terrainSize = data.size;
            terrainSize.y = 0;
            for (int y = 0; y < heightMapResolution; y += 2)
            {
                for (int x = 0; x < heightMapResolution; x += 2)
                {
                    if (heights[x, y] < terrainSize.y) continue;
                    terrainSize.y = heights[x, y];
                }
            }

            this.diffuseLayers = new Texture2D[3];
            this.normalLayers = new Texture2D[3];
            this.tileSizes = new Vector2[3];

            for (int y = 0; y < textureHeight; y ++)
            {
                for (int x = 0; x < textureWidth; x ++)
                {
                    Color finalColor = Color.black;
                    if (layers.Length >= 1) finalColor.r = alphaMap[y, x, 0];
                    if (layers.Length >= 2) finalColor.g = alphaMap[y, x, 1];
                    if (layers.Length >= 3) finalColor.b = alphaMap[y, x, 2];

                    int xPosition = (int)((float)x / textureWidth * heightMapResolution);
                    int yPosition = (int)((float)y / textureHeight * heightMapResolution);

                    finalColor.a = heights[yPosition, xPosition] / terrainSize.y;

                    controlColors[x + textureWidth * y] = finalColor;
                }
            }


            control.SetPixels(0, 0, textureWidth, textureHeight, controlColors.ToArray());
            control.Apply();


            terrainSize.y *= data.size.y;
            for (int i = 0; i < Mathf.Min(layers.Length, this.diffuseLayers.Length); i++)
            {
                this.diffuseLayers[i] = layers[i].diffuseTexture;
                this.normalLayers[i] = layers[i].normalMapTexture;
                this.tileSizes[i] = layers[i].tileSize;
            }

            IsSampling = false;
            controlColors.Dispose();
            _sampledTerrains[terrain] = this;
        }
    }
}