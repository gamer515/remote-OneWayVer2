using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 청크의 평평한 Terrain 위에 Dungeon 바닥 타일을 결정적으로 랜덤 배치합니다.
/// GameObject를 생성하지 않고 GPU Instancing으로 렌더링합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DungeonTileSurface : MonoBehaviour
{
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject floorDetailPrefab;
    [SerializeField, Min(0.1f)] private float tileSize = 2.8f;
    [SerializeField, Range(0f, 1f)] private float detailChance = 0.2f;
    [SerializeField] private float surfaceOffsetY = 0.02f;
    [SerializeField] private int baseSeed = 1729;

    private Mesh floorMesh;
    private Mesh detailMesh;
    private Material floorMaterial;
    private Material detailMaterial;
    private Matrix4x4 floorSourceMatrix;
    private Matrix4x4 detailSourceMatrix;
    private readonly List<Matrix4x4> floorMatrices = new List<Matrix4x4>();
    private readonly List<Matrix4x4> detailMatrices = new List<Matrix4x4>();
    private int chunkIndex;

    public void ConfigureChunk(int globalChunkIndex)
    {
        chunkIndex = Mathf.Max(0, globalChunkIndex);
        if (floorMesh == null && detailMesh == null)
            ResolveSources();
        RebuildLayout();
    }

    private void Awake()
    {
        ResolveSources();
        RebuildLayout();
    }

    private void OnEnable()
    {
        if (floorMesh == null) ResolveSources();
        if (floorMatrices.Count == 0 && detailMatrices.Count == 0) RebuildLayout();
    }

    private void LateUpdate()
    {
        DrawInstances(floorMesh, floorMaterial, floorMatrices);
        DrawInstances(detailMesh, detailMaterial, detailMatrices);
    }

    private void OnDestroy()
    {
        if (floorMaterial != null) Destroy(floorMaterial);
        if (detailMaterial != null && detailMaterial != floorMaterial) Destroy(detailMaterial);
    }

    private void ResolveSources()
    {
        ResolveSource(floorPrefab, out floorMesh, out floorMaterial, out floorSourceMatrix);
        ResolveSource(
            floorDetailPrefab,
            out detailMesh,
            out detailMaterial,
            out detailSourceMatrix);
    }

    private static void ResolveSource(
        GameObject prefab,
        out Mesh mesh,
        out Material runtimeMaterial,
        out Matrix4x4 sourceMatrix)
    {
        mesh = null;
        runtimeMaterial = null;
        sourceMatrix = Matrix4x4.identity;
        if (prefab == null) return;

        MeshFilter filter = prefab.GetComponentInChildren<MeshFilter>(true);
        MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>(true);
        if (filter == null || renderer == null || filter.sharedMesh == null ||
            renderer.sharedMaterial == null)
            return;

        mesh = filter.sharedMesh;
        runtimeMaterial = new Material(renderer.sharedMaterial)
        {
            enableInstancing = true,
            name = renderer.sharedMaterial.name + " (Dungeon Instanced)"
        };
        sourceMatrix = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
    }

    private void RebuildLayout()
    {
        floorMatrices.Clear();
        detailMatrices.Clear();
        Terrain terrain = GetComponentInChildren<Terrain>(true);
        if (terrain == null || terrain.terrainData == null || tileSize <= 0f) return;

        Vector3 size = terrain.terrainData.size;
        int columns = Mathf.Max(1, Mathf.RoundToInt(size.x / tileSize));
        int rows = Mathf.Max(1, Mathf.RoundToInt(size.z / tileSize));
        float startX = terrain.transform.localPosition.x +
            (size.x - (columns - 1) * tileSize) * 0.5f;
        float startZ = terrain.transform.localPosition.z +
            (size.z - (rows - 1) * tileSize) * 0.5f;
        var random = new System.Random(CreateSeed(baseSeed, chunkIndex));

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                bool useDetail = random.NextDouble() < detailChance;
                float rotationY = random.Next(0, 4) * 90f;
                Matrix4x4 cellMatrix = transform.localToWorldMatrix * Matrix4x4.TRS(
                    new Vector3(
                        startX + column * tileSize,
                        terrain.transform.localPosition.y + surfaceOffsetY,
                        startZ + row * tileSize),
                    Quaternion.Euler(0f, rotationY, 0f),
                    Vector3.one);

                if (useDetail && detailMesh != null)
                    detailMatrices.Add(cellMatrix * detailSourceMatrix);
                else if (floorMesh != null)
                    floorMatrices.Add(cellMatrix * floorSourceMatrix);
            }
        }
    }

    private static int CreateSeed(int seed, int index)
    {
        unchecked { return seed * 397 ^ index; }
    }

    private void DrawInstances(
        Mesh mesh,
        Material material,
        List<Matrix4x4> matrices)
    {
        if (mesh == null || material == null || matrices.Count == 0) return;

        const int maximumBatchSize = 1023;
        Matrix4x4[] batch = new Matrix4x4[Mathf.Min(maximumBatchSize, matrices.Count)];
        for (int start = 0; start < matrices.Count; start += maximumBatchSize)
        {
            int count = Mathf.Min(maximumBatchSize, matrices.Count - start);
            matrices.CopyTo(start, batch, 0, count);
            Graphics.DrawMeshInstanced(
                mesh,
                0,
                material,
                batch,
                count,
                null,
                ShadowCastingMode.On,
                true,
                gameObject.layer);
        }
    }
}
