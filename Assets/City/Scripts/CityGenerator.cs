using System.Collections.Generic;
using UnityEngine;

// 윤곽선 스타일의 거대 도시를 절차적으로 생성.
// 인스펙터 우클릭(⋮) 메뉴 → Generate City / Clear City
public class CityGenerator : MonoBehaviour
{
    [Header("크기")]
    public int blocksX = 16;
    public int blocksZ = 16;
    public float blockSize = 80f;      // 블록 한 변 (m)
    public float roadWidth = 18f;      // 도로 폭 (m)
    public float sidewalkHeight = 0.3f;

    [Header("건물")]
    public float minLot = 14f;         // 건물 부지 최소 크기
    public float maxLot = 38f;
    public float setback = 2.5f;       // 부지 경계에서 들여쓰기
    public float minHeight = 8f;
    public float maxHeight = 160f;
    [Range(0, 1)] public float towerTierChance = 0.35f; // 위층이 좁아지는 계단형 건물 비율
    [Range(0, 1)] public float parkChance = 0.07f;
    public int seed = 12345;

    [Header("인도 (블록 가장자리, 도로와 맞닿은 부분)")]
    public float walkwayWidth = 3.5f;  // 건물은 블록 가장자리에서 2 + setback 만큼 안쪽에 있으므로 그보다 좁게
    public float walkwayHeight = 0.45f;
    public Color walkwayColor = new Color(0.80f, 0.78f, 0.74f);

    [Header("머티리얼")]
    public Material buildingMat;
    public Material sidewalkMat;
    public Material parkMat;
    public Material groundMat;

    Mesh cube;
    System.Random rng;

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    [ContextMenu("Clear City")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
    }

    [ContextMenu("Generate City")]
    public void Generate()
    {
        Clear();
        rng = new System.Random(seed);
        EnsureCube();

        float pitch = blockSize + roadWidth;
        float sizeX = blocksX * pitch + roadWidth;
        float sizeZ = blocksZ * pitch + roadWidth;
        Vector3 origin = transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f;

        // 바닥(도로)
        Box("Ground", transform, origin + new Vector3(sizeX / 2, -0.5f, sizeZ / 2), new Vector3(sizeX + 400, 1f, sizeZ + 400), groundMat, null);

        var blocksRoot = new GameObject("Blocks").transform;
        blocksRoot.SetParent(transform, false);
        var mpb = new MaterialPropertyBlock();
        Vector2 center = new Vector2(sizeX, sizeZ) * 0.5f;
        float maxDist = center.magnitude;
        int count = 0;
        int buildingLayer = Mathf.Max(0, LayerMask.NameToLayer("Building"));

        for (int bx = 0; bx < blocksX; bx++)
        for (int bz = 0; bz < blocksZ; bz++)
        {
            float x0 = roadWidth + bx * pitch, z0 = roadWidth + bz * pitch;
            var block = new GameObject($"Block_{bx}_{bz}").transform;
            block.SetParent(blocksRoot, false);
            Vector3 bc = origin + new Vector3(x0 + blockSize / 2, 0, z0 + blockSize / 2);

            bool park = rng.NextDouble() < parkChance;
            Box("Sidewalk", block, bc + Vector3.up * sidewalkHeight / 2, new Vector3(blockSize, sidewalkHeight, blockSize), park ? parkMat : sidewalkMat, null);
            AddWalkways(block, origin + new Vector3(x0, 0, z0));
            if (park) continue;

            // 도심일수록 높게
            float dist = Vector2.Distance(new Vector2(x0 + blockSize / 2, z0 + blockSize / 2), center) / maxDist;
            float downtown = Mathf.Clamp01(1f - dist * 1.6f);

            var lots = new List<Rect>();
            Split(new Rect(x0 + 2f, z0 + 2f, blockSize - 4f, blockSize - 4f), lots, 0);
            foreach (var lot in lots)
            {
                float w = lot.width - setback * 2, d = lot.height - setback * 2;
                if (w < 4 || d < 4) continue;
                float hMax = Mathf.Lerp(minHeight * 2.5f, maxHeight, downtown * downtown);
                float h = R(minHeight, hMax) * R(0.6f, 1.1f);
                h = Mathf.Max(minHeight, Mathf.Round(h / 3.5f) * 3.5f); // 층 단위(3.5m)
                Vector3 pos = origin + new Vector3(lot.center.x, sidewalkHeight + h / 2, lot.center.y);

                float g = R(0.9f, 1f);
                mpb.SetColor("_Color", new Color(g, g, g * R(0.97f, 1.02f), 1));
                var b = Box("Building", block, pos, new Vector3(w, h, d), buildingMat, mpb);
                b.layer = buildingLayer;
                count++;

                // 계단형 상층부
                if (h > 30 && rng.NextDouble() < towerTierChance)
                {
                    float tw = w * R(0.45f, 0.75f), td = d * R(0.45f, 0.75f), th = h * R(0.25f, 0.6f);
                    Vector3 tp = origin + new Vector3(lot.center.x, sidewalkHeight + h + th / 2, lot.center.y);
                    var top = Box("Tier", b.transform, tp, new Vector3(tw, th, td), buildingMat, mpb);
                    top.layer = buildingLayer;
                }
            }
        }
        Debug.Log($"[CityGenerator] {blocksX}x{blocksZ} blocks, {count} buildings, {sizeX:F0} x {sizeZ:F0} m");
    }

    // 이미 만들어진 도시에 인도만 추가 (건물은 그대로). 인스펙터 우클릭(⋮) 메뉴 → Add Walkways
    [ContextMenu("Add Walkways")]
    public void AddWalkwaysToExisting()
    {
        EnsureCube();
        var blocksRoot = transform.Find("Blocks");
        if (!blocksRoot) return;
        float pitch = blockSize + roadWidth;
        Vector3 origin = transform.position - new Vector3(blocksX * pitch + roadWidth, 0, blocksZ * pitch + roadWidth) * 0.5f;
        for (int bx = 0; bx < blocksX; bx++)
        for (int bz = 0; bz < blocksZ; bz++)
        {
            var block = blocksRoot.Find($"Block_{bx}_{bz}");
            if (!block) continue;
            for (int i = block.childCount - 1; i >= 0; i--)
                if (block.GetChild(i).name == "Walkway") { var c = block.GetChild(i).gameObject; if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            AddWalkways(block, origin + new Vector3(roadWidth + bx * pitch, 0, roadWidth + bz * pitch));
        }
    }

    // 블록 네 변을 따라 인도 (min = 블록 모서리 월드 좌표)
    void AddWalkways(Transform block, Vector3 min)
    {
        float w = walkwayWidth, s = blockSize, y = transform.position.y + walkwayHeight / 2;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_Color", walkwayColor);
        Box("Walkway", block, new Vector3(min.x + s / 2, y, min.z + w / 2), new Vector3(s, walkwayHeight, w), sidewalkMat, mpb);
        Box("Walkway", block, new Vector3(min.x + s / 2, y, min.z + s - w / 2), new Vector3(s, walkwayHeight, w), sidewalkMat, mpb);
        Box("Walkway", block, new Vector3(min.x + w / 2, y, min.z + s / 2), new Vector3(w, walkwayHeight, s - 2 * w), sidewalkMat, mpb);
        Box("Walkway", block, new Vector3(min.x + s - w / 2, y, min.z + s / 2), new Vector3(w, walkwayHeight, s - 2 * w), sidewalkMat, mpb);
    }

    void EnsureCube()
    {
        if (cube) return;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube = tmp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Destroy(tmp); else DestroyImmediate(tmp);
    }

    void Split(Rect r, List<Rect> outLots, int depth)
    {
        bool canX = r.width > maxLot || (r.width > minLot * 2 && rng.NextDouble() < 0.5);
        bool canZ = r.height > maxLot || (r.height > minLot * 2 && rng.NextDouble() < 0.5);
        if (depth > 6 || (!canX && !canZ)) { outLots.Add(r); return; }
        bool splitX = canX && (!canZ || r.width >= r.height);
        if (splitX)
        {
            float s = R(minLot, r.width - minLot);
            Split(new Rect(r.x, r.y, s, r.height), outLots, depth + 1);
            Split(new Rect(r.x + s, r.y, r.width - s, r.height), outLots, depth + 1);
        }
        else
        {
            float s = R(minLot, r.height - minLot);
            Split(new Rect(r.x, r.y, r.width, s), outLots, depth + 1);
            Split(new Rect(r.x, r.y + s, r.width, r.height - s), outLots, depth + 1);
        }
    }

    GameObject Box(string name, Transform parent, Vector3 worldPos, Vector3 size, Material mat, MaterialPropertyBlock mpb)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = worldPos;
        // 부모 스케일 영향 제거
        Vector3 ps = parent.lossyScale;
        go.transform.localScale = new Vector3(size.x / ps.x, size.y / ps.y, size.z / ps.z);
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        if (mpb != null) mr.SetPropertyBlock(mpb);
        go.AddComponent<BoxCollider>();
        return go;
    }
}
