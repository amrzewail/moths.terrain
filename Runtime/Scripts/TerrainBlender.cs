using UnityEngine;

namespace Moths.Terrain.Blending
{
    using System;
    using System.IO;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    [RequireComponent(typeof(Terrain))]
    [ExecuteInEditMode]
    public class TerrainBlender : MonoBehaviour
    {
        [SerializeField] TerrainTextureSampler _sampler;

        private Terrain _terrain;

        public Terrain Terrain => _terrain ? _terrain : _terrain = GetComponent<Terrain>();

        private void Awake()
        {
            if (_sampler == null) _sampler = new TerrainTextureSampler();
            _sampler.RegisterSampler(Terrain);
        }

        public void GetSampler(ref TerrainTextureSampler sampler)
        {
            if (_sampler == null) _sampler = new TerrainTextureSampler();
            sampler = _sampler;
        }
    }
}