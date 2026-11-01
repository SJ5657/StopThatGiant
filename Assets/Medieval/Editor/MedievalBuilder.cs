using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 중세 버전 만들기 (메뉴 Tools → Medieval):
//  - Build Prefabs: 투석기·트레뷰셋(탱크 대신), 경비대 마차(경찰차 대신), 말수레(자동차 대신) 프리팹을 상자 조각으로 만듦
//  - Setup Scene: 지금 열린 씬(Medieval.unity)을 중세 버전으로 설정 (적·차 프리팹 교체, 헬기·전투기·UFO 끄기, 초고층 끄기 등)
// 모델은 Unity 기본 상자 + OutlineBox 재질(윤곽선)이라 외부 에셋이 필요 없음.
public static class MedievalBuilder
{
    const string Root = "Assets/Medieval";
    const string MatDir = Root + "/Materials";
    const string PrefabDir = Root + "/Prefabs";

    // ───────────── 재질 ─────────────
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    // 작은 소품용 OutlineBox 재질 (윤곽선 얇게, 창문 없음)
    static Material Mat(string name, Color c)
    {
        if (mats.TryGetValue(name, out var m) && m) return m;
        string path = $"{MatDir}/Prop_{name}.mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m)
        {
            m = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/City/Materials/Building.mat"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        m.SetFloat("_Windows", 0f); m.SetFloat("_Timber", 0f); m.SetFloat("_Stone", 0f); m.SetFloat("_Cobble", 0f);
        m.SetFloat("_LineWidth", 0.07f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return mats[name] = m;
    }

    static Material WoodDark => Mat("WoodDark", new Color(0.40f, 0.28f, 0.18f));
    static Material WoodLight => Mat("WoodLight", new Color(0.64f, 0.47f, 0.30f));
    static Material Iron => Mat("Iron", new Color(0.34f, 0.35f, 0.38f));
    static Material Stone => Mat("Stone", new Color(0.62f, 0.60f, 0.57f));
    static Material Canvas => Mat("Canvas", new Color(0.93f, 0.89f, 0.78f));
    static Material Hay => Mat("Hay", new Color(0.88f, 0.74f, 0.38f));
    static Material Sack => Mat("Sack", new Color(0.80f, 0.70f, 0.52f));
    static Material RedCloth => Mat("RedCloth", new Color(0.72f, 0.16f, 0.15f));
    static Material BlueCloth => Mat("BlueCloth", new Color(0.20f, 0.32f, 0.64f));
    static Material HorseBrown => Mat("HorseBrown", new Color(0.52f, 0.34f, 0.20f));
    static Material HorseWhite => Mat("HorseWhite", new Color(0.88f, 0.86f, 0.82f));
    static Material HorseBlack => Mat("HorseBlack", new Color(0.20f, 0.18f, 0.17f));
    static Material HorseGray => Mat("HorseGray", new Color(0.58f, 0.57f, 0.56f));

    // ───────────── 조각 ─────────────
    static GameObject Cube(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = size;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        return go;
    }

    static Transform Empty(string name, Transform parent, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        return t;
    }

    // 바퀴: 얇은 판 네 장을 45°씩 돌려 겹친 팔각형 (윤곽선이 바퀴살처럼 보임). 축은 X 방향
    static void Wheel(Transform parent, Vector3 pos, float dia, float thick)
    {
        var w = Empty("Wheel", parent, pos);
        float side = dia * 0.4142f;
        for (int i = 0; i < 4; i++) Cube("Rim", w, Vector3.zero, new Vector3(thick, dia, side), WoodDark, new Vector3(i * 45f, 0f, 0f));
        Cube("Hub", w, Vector3.zero, new Vector3(thick * 1.6f, dia * 0.22f, dia * 0.22f), Iron);
    }

    // 말 (앞 = +Z). 발굽 아래가 pos
    static void Horse(Transform parent, Vector3 pos, Material coat, Material mane)
    {
        var h = Empty("Horse", parent, pos);
        Cube("Body", h, new Vector3(0f, 1.55f, 0f), new Vector3(0.85f, 0.95f, 2.1f), coat);
        foreach (var x in new[] { -0.28f, 0.28f })
            foreach (var z in new[] { -0.75f, 0.75f })
                Cube("Leg", h, new Vector3(x, 0.55f, z), new Vector3(0.24f, 1.1f, 0.24f), coat);
        Cube("Neck", h, new Vector3(0f, 2.15f, 1.05f), new Vector3(0.42f, 1.05f, 0.5f), coat, new Vector3(35f, 0f, 0f));
        Cube("Head", h, new Vector3(0f, 2.55f, 1.5f), new Vector3(0.36f, 0.42f, 0.85f), coat, new Vector3(20f, 0f, 0f));
        Cube("Mane", h, new Vector3(0f, 2.3f, 0.92f), new Vector3(0.12f, 0.9f, 0.32f), mane, new Vector3(35f, 0f, 0f));
        Cube("Tail", h, new Vector3(0f, 1.55f, -1.18f), new Vector3(0.14f, 0.9f, 0.18f), mane, new Vector3(-25f, 0f, 0f));
    }

    // 말이 끄는 수레 바닥 (앞 = +Z): 짐칸·옆판·끌채·바퀴
    static Transform CartBase(string name, Material coat, Material mane)
    {
        var root = new GameObject(name).transform;
        Horse(root, new Vector3(0f, 0f, 1.9f), coat, mane);
        Cube("Bed", root, new Vector3(0f, 1.05f, -1.4f), new Vector3(1.9f, 0.3f, 3.0f), WoodLight);
        foreach (var x in new[] { -0.9f, 0.9f }) Cube("Side", root, new Vector3(x, 1.42f, -1.4f), new Vector3(0.1f, 0.45f, 3.0f), WoodDark);
        Cube("Back", root, new Vector3(0f, 1.42f, -2.85f), new Vector3(1.9f, 0.45f, 0.1f), WoodDark);
        foreach (var x in new[] { -0.55f, 0.55f }) Cube("Shaft", root, new Vector3(x, 1.25f, 0.4f), new Vector3(0.1f, 0.1f, 2.4f), WoodDark);
        foreach (var x in new[] { -1.05f, 1.05f }) Wheel(root, new Vector3(x, 0.68f, -1.4f), 1.36f, 0.12f);
        return root;
    }

    // ───────────── 프리팹 ─────────────
    [MenuItem("Tools/Medieval/Build Prefabs")]
    public static void BuildPrefabs()
    {
        if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder(Root, "Prefabs");
        mats.Clear();
        Save(Catapult(), "Catapult");
        Save(Trebuchet(), "Trebuchet");
        Save(GuardWagon(), "GuardWagon");
        Save(TroopWagon(), "TroopWagon");
        Save(HayCart(), "Cart_Hay");
        Save(BarrelCart(), "Cart_Barrels");
        Save(SackCart(), "Cart_Sacks");
        Save(CoveredCart(), "Cart_Covered");
        Save(Carriage(), "Carriage");
        Save(Griffin(), "GriffinKnight");
        Save(Drake("Wyvern", 1f, false), "Wyvern");
        Save(Drake("Dragon", 2.2f, true), "Dragon");
        Save(MageIsland(), "MageIsland");
        BoulderMaterial();
        AssetDatabase.SaveAssets();
        Debug.Log("[MedievalBuilder] 프리팹 완료: " + PrefabDir);
    }

    static void Save(GameObject go, string name)
    {
        PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
        Object.DestroyImmediate(go);
    }

    public static Material BoulderMaterial()
    {
        var m = Mat("Boulder", new Color(0.55f, 0.53f, 0.50f));
        return m;
    }

    // 투석기 (탱크 대신): 바퀴 달린 받침 위에서 도는 발사대(Turret), 그 위에 뒤로 젖힌 팔이 쏠 때마다 앞으로 휘둘러짐
    static GameObject Catapult()
    {
        var root = new GameObject("Catapult");
        foreach (var x in new[] { -2.4f, 2.4f }) Cube("Rail", root.transform, new Vector3(x, 1.5f, 0f), new Vector3(0.6f, 0.6f, 9.5f), WoodDark);
        foreach (var z in new[] { -4.2f, 0f, 4.2f }) Cube("Cross", root.transform, new Vector3(0f, 1.5f, z), new Vector3(5.4f, 0.5f, 0.5f), WoodDark);
        foreach (var x in new[] { -2.95f, 2.95f })
            foreach (var z in new[] { -3.3f, 3.3f }) Wheel(root.transform, new Vector3(x, 1.4f, z), 2.8f, 0.45f);

        var turret = Empty("Turret", root.transform, new Vector3(0f, 2.0f, 0f));
        Cube("Platform", turret, new Vector3(0f, 0f, 0f), new Vector3(4.2f, 0.4f, 6.4f), WoodLight);
        foreach (var x in new[] { -1.25f, 1.25f }) Cube("Upright", turret, new Vector3(x, 4.2f, 1.6f), new Vector3(0.55f, 8.2f, 0.55f), WoodDark);
        Cube("Crossbar", turret, new Vector3(0f, 7.9f, 1.6f), new Vector3(3.1f, 0.55f, 0.55f), WoodDark);
        Cube("Pad", turret, new Vector3(0f, 7.9f, 1.25f), new Vector3(1.2f, 0.8f, 0.3f), Sack);
        foreach (var x in new[] { -1.25f, 1.25f }) Cube("Brace", turret, new Vector3(x, 2.5f, 0.4f), new Vector3(0.4f, 5.4f, 0.4f), WoodDark, new Vector3(30f, 0f, 0f)); // 뒤에서 기둥을 받침
        Cube("Axle", turret, new Vector3(0f, 1.0f, 0f), new Vector3(2.4f, 0.5f, 0.5f), Iron);
        var arm = Empty("Arm", turret, new Vector3(0f, 1.0f, 0f));
        Cube("Beam", arm, new Vector3(0f, 4.25f, 0f), new Vector3(0.5f, 8.5f, 0.5f), WoodLight);
        Cube("Bucket", arm, new Vector3(0f, 8.5f, 0f), new Vector3(1.5f, 0.5f, 1.5f), WoodDark);
        Cube("Stone", arm, new Vector3(0f, 9.1f, 0f), new Vector3(1.1f, 1.0f, 1.1f), Stone, new Vector3(20f, 30f, 10f));
        var muzzle = Empty("Muzzle", turret, new Vector3(0f, 10.5f, 2.6f));

        var ai = root.AddComponent<TankAI>();
        ai.turret = turret; ai.muzzle = muzzle;
        ai.minRange = 90f; ai.maxRange = 220f;
        ai.moveSpeed = 45f; ai.turnSpeed = 200f; ai.turretTurnSpeed = 70f;
        ai.fireInterval = 3f; ai.shellSpeed = 115f; ai.damage = 85f; ai.spread = 8f; ai.maxHP = 60f;
        ai.scoreLabel = "투석기";
        var ca = root.AddComponent<CatapultArm>();
        ca.arm = arm; ca.restAngle = -75f; ca.throwAngle = 13f;
        arm.localEulerAngles = new Vector3(ca.restAngle, 0f, 0f); // 편집기에서도 쏘기 전 자세로 보이게
        Finish(root, new Vector3(6.6f, 8f, 10f), new Vector3(0f, 4f, 0f));
        return root;
    }

    // 트레뷰셋 (중전차 대신): 높은 A자 틀, 긴 팔 끝에 큰 추. 멀리서 느리지만 빠르고 아픈 돌을 던짐
    static GameObject Trebuchet()
    {
        var root = new GameObject("Trebuchet");
        foreach (var x in new[] { -2.8f, 2.8f }) Cube("Rail", root.transform, new Vector3(x, 1.2f, 0f), new Vector3(0.7f, 0.7f, 13f), WoodDark);
        foreach (var z in new[] { -5.8f, 0f, 5.8f }) Cube("Cross", root.transform, new Vector3(0f, 1.2f, z), new Vector3(6.3f, 0.6f, 0.6f), WoodDark);
        foreach (var x in new[] { -3.3f, 3.3f })
            foreach (var z in new[] { -4.6f, 4.6f }) Wheel(root.transform, new Vector3(x, 1.2f, z), 2.4f, 0.45f);

        var turret = Empty("Turret", root.transform, new Vector3(0f, 1.6f, 0f));
        Cube("Platform", turret, Vector3.zero, new Vector3(5.2f, 0.4f, 8f), WoodLight);
        // A자 틀 (양쪽에 두 다리)
        foreach (var x in new[] { -1.6f, 1.6f })
        {
            Cube("Leg", turret, new Vector3(x, 6.3f, -2.0f), new Vector3(0.6f, 13.2f, 0.6f), WoodDark, new Vector3(17.6f, 0f, 0f)); // 위쪽이 굴대에서 만남
            Cube("Leg", turret, new Vector3(x, 6.3f, 2.0f), new Vector3(0.6f, 13.2f, 0.6f), WoodDark, new Vector3(-17.6f, 0f, 0f));
        }
        Cube("Axle", turret, new Vector3(0f, 12.6f, 0f), new Vector3(4f, 0.6f, 0.6f), Iron);
        var arm = Empty("Arm", turret, new Vector3(0f, 12.6f, 0f));
        Cube("Beam", arm, new Vector3(0f, 5f, 0f), new Vector3(0.6f, 18f, 0.6f), WoodLight);
        Cube("Weight", arm, new Vector3(0f, -4.2f, 0f), new Vector3(2.8f, 2.8f, 2.8f), Iron);
        Cube("Sling", arm, new Vector3(0f, 14.2f, 0f), new Vector3(0.9f, 0.9f, 0.9f), Stone, new Vector3(15f, 25f, 0f));
        var muzzle = Empty("Muzzle", turret, new Vector3(0f, 24f, 4f));

        var ai = root.AddComponent<TankAI>();
        ai.turret = turret; ai.muzzle = muzzle;
        ai.minRange = 140f; ai.maxRange = 320f;
        ai.moveSpeed = 32f; ai.turnSpeed = 120f; ai.turretTurnSpeed = 45f;
        ai.fireInterval = 4.5f; ai.shellSpeed = 165f; ai.damage = 140f; ai.spread = 10f; ai.maxHP = 90f;
        ai.scoreLabel = "트레뷰셋";
        var ca = root.AddComponent<CatapultArm>();
        ca.arm = arm; ca.restAngle = -150f; ca.throwAngle = 35f; ca.throwTime = 0.35f; ca.reloadTime = 2.6f;
        arm.localEulerAngles = new Vector3(ca.restAngle, 0f, 0f);
        Finish(root, new Vector3(7.6f, 16f, 14f), new Vector3(0f, 8f, 0f));
        return root;
    }

    // 적 유닛 공통: Military 레이어 + 충돌 상자
    static void Finish(GameObject root, Vector3 size, Vector3 center)
    {
        int layer = LayerMask.NameToLayer("Military");
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        var box = root.AddComponent<BoxCollider>();
        box.size = size; box.center = center;
    }

    // 경비대 마차 (경찰차 대신): 흰 말 + 포장 덮인 짐칸 + 빨간 깃발
    static GameObject GuardWagon()
    {
        var root = CartBase("GuardWagon", HorseWhite, HorseGray);
        Cube("Cover", root, new Vector3(0f, 2.15f, -1.45f), new Vector3(1.95f, 1.2f, 2.7f), Canvas);
        Cube("CoverTop", root, new Vector3(0f, 2.85f, -1.45f), new Vector3(1.4f, 0.3f, 2.7f), Canvas);
        Cube("Stripe", root, new Vector3(0f, 2.15f, -1.45f), new Vector3(2.0f, 0.3f, 2.72f), BlueCloth);
        Cube("Pole", root, new Vector3(0.8f, 3.1f, -2.7f), new Vector3(0.08f, 2.4f, 0.08f), WoodDark);
        Cube("Banner", root, new Vector3(0.8f, 3.85f, -3.15f), new Vector3(0.05f, 0.8f, 0.85f), RedCloth);
        Cube("Seat", root, new Vector3(0f, 1.55f, -0.1f), new Vector3(1.4f, 0.25f, 0.5f), WoodDark);
        return root.gameObject;
    }

    // 병력 마차: 검은 말 + 덮개 없는 짐칸 + 옆 난간에 붉은 방패 + 깃발 (궁수는 TroopWagonSpawner가 태움)
    static GameObject TroopWagon()
    {
        var root = CartBase("TroopWagon", HorseBlack, HorseGray);
        foreach (var x in new[] { -0.9f, 0.9f })
        {
            Cube("Rail", root, new Vector3(x, 1.95f, -1.4f), new Vector3(0.08f, 0.1f, 3.0f), WoodDark);
            foreach (var z in new[] { -2.4f, -1.4f, -0.4f }) Cube("Shield", root, new Vector3(x * 1.06f, 1.55f, z), new Vector3(0.06f, 0.75f, 0.7f), RedCloth);
        }
        Cube("Pole", root, new Vector3(-0.8f, 3.1f, -2.8f), new Vector3(0.08f, 2.4f, 0.08f), WoodDark);
        Cube("Banner", root, new Vector3(-0.8f, 3.85f, -3.25f), new Vector3(0.05f, 0.8f, 0.85f), RedCloth);
        Cube("Seat", root, new Vector3(0f, 1.55f, 0.05f), new Vector3(1.4f, 0.25f, 0.3f), WoodDark);
        return root.gameObject;
    }

    // 프리팹 전체를 다시 만들지 않고 병력 마차만 저장
    [MenuItem("Tools/Medieval/Build Troop Wagon")]
    public static void BuildTroopWagon()
    {
        mats.Clear();
        Save(TroopWagon(), "TroopWagon");
        AssetDatabase.SaveAssets();
    }

    static GameObject HayCart()
    {
        var root = CartBase("Cart_Hay", HorseBrown, HorseBlack);
        Cube("Hay", root, new Vector3(0f, 1.95f, -1.4f), new Vector3(1.75f, 1.2f, 2.8f), Hay);
        Cube("HayTop", root, new Vector3(0f, 2.65f, -1.5f), new Vector3(1.3f, 0.35f, 2.3f), Hay);
        return root.gameObject;
    }

    static GameObject BarrelCart()
    {
        var root = CartBase("Cart_Barrels", HorseBlack, HorseBlack);
        foreach (var z in new[] { -2.3f, -1.4f, -0.5f })
            foreach (var x in new[] { -0.45f, 0.45f })
            {
                Cube("Barrel", root, new Vector3(x, 1.7f, z), new Vector3(0.75f, 1.0f, 0.75f), WoodLight);
                Cube("Hoop", root, new Vector3(x, 1.7f, z), new Vector3(0.8f, 0.12f, 0.8f), Iron);
            }
        return root.gameObject;
    }

    static GameObject SackCart()
    {
        var root = CartBase("Cart_Sacks", HorseGray, HorseBlack);
        foreach (var z in new[] { -2.2f, -1.4f, -0.6f })
            foreach (var x in new[] { -0.42f, 0.42f })
                Cube("Sack", root, new Vector3(x, 1.55f, z), new Vector3(0.7f, 0.55f, 0.65f), Sack, new Vector3(0f, (x + z) * 17f, 0f));
        Cube("Sack", root, new Vector3(0f, 2.05f, -1.4f), new Vector3(0.7f, 0.5f, 0.9f), Sack, new Vector3(0f, 12f, 0f));
        return root.gameObject;
    }

    static GameObject CoveredCart()
    {
        var root = CartBase("Cart_Covered", HorseBrown, HorseBrown);
        Cube("Cover", root, new Vector3(0f, 2.15f, -1.45f), new Vector3(1.95f, 1.2f, 2.7f), Canvas);
        Cube("CoverTop", root, new Vector3(0f, 2.85f, -1.45f), new Vector3(1.4f, 0.3f, 2.7f), Canvas);
        return root.gameObject;
    }

    // 귀족 마차 (버스 대신, 조금 큼): 말 두 마리 + 지붕 달린 객차
    static GameObject Carriage()
    {
        var root = new GameObject("Carriage").transform;
        Horse(root, new Vector3(-0.55f, 0f, 2.4f), HorseBlack, HorseBlack);
        Horse(root, new Vector3(0.55f, 0f, 2.4f), HorseBlack, HorseBlack);
        Cube("Cabin", root, new Vector3(0f, 2.1f, -1.4f), new Vector3(2.0f, 1.9f, 2.6f), RedCloth);
        Cube("Roof", root, new Vector3(0f, 3.15f, -1.4f), new Vector3(2.2f, 0.25f, 2.8f), WoodDark);
        foreach (var x in new[] { -1.01f, 1.01f }) Cube("Window", root, new Vector3(x, 2.35f, -1.4f), new Vector3(0.05f, 0.7f, 1.1f), Iron);
        Cube("Seat", root, new Vector3(0f, 2.0f, 0.3f), new Vector3(1.6f, 0.3f, 0.7f), WoodDark);
        Cube("Floor", root, new Vector3(0f, 1.1f, -0.9f), new Vector3(1.8f, 0.25f, 3.8f), WoodDark);
        foreach (var x in new[] { -1.15f, 1.15f })
        {
            Wheel(root, new Vector3(x, 0.75f, -2.2f), 1.5f, 0.12f);
            Wheel(root, new Vector3(x, 0.55f, 0.3f), 1.1f, 0.12f);
        }
        foreach (var x in new[] { -0.2f, 0.2f }) Cube("Pole", root, new Vector3(x, 1.25f, 1.4f), new Vector3(0.1f, 0.1f, 2.4f), WoodDark);
        return root.gameObject;
    }

    // ───────────── 하늘의 적 (헬기·전투기·UFO 자리) ─────────────
    static Transform Pivot(Transform parent, Vector3 pos) => Empty("Wing", parent, pos);

    // 그리폰 기사 (헬기 대신): 사자 몸 + 독수리 머리 그리폰에 창 든 기사. 빠르게 선회하며 창을 자주 던짐 (곧게 날아감, 약함)
    static GameObject Griffin()
    {
        var root = new GameObject("GriffinKnight");
        var t = root.transform;
        Material tawny = Mat("GriffinTawny", new Color(0.80f, 0.62f, 0.36f)), white = Mat("GriffinWhite", new Color(0.95f, 0.94f, 0.90f));
        Material beak = Mat("Beak", new Color(0.96f, 0.76f, 0.22f)), feather = Mat("Feather", new Color(0.56f, 0.39f, 0.23f));
        Material armor = Mat("Armor", new Color(0.76f, 0.78f, 0.82f));
        Cube("Body", t, Vector3.zero, new Vector3(3.2f, 3.0f, 7f), tawny);
        Cube("Chest", t, new Vector3(0f, 0.3f, 3.2f), new Vector3(3.0f, 3.2f, 2.2f), white);
        Cube("Neck", t, new Vector3(0f, 1.8f, 4.3f), new Vector3(1.8f, 2.6f, 1.8f), white, new Vector3(25f, 0f, 0f));
        Cube("Head", t, new Vector3(0f, 3.0f, 5.1f), new Vector3(1.8f, 1.8f, 2.2f), white);
        Cube("Beak", t, new Vector3(0f, 2.8f, 6.5f), new Vector3(0.8f, 0.8f, 1.4f), beak, new Vector3(20f, 0f, 0f));
        foreach (var x in new[] { -1.1f, 1.1f })
        {
            Cube("Talon", t, new Vector3(x, -2.4f, 2.5f), new Vector3(0.7f, 2.6f, 0.7f), beak);
            Cube("Leg", t, new Vector3(x, -2.4f, -2.5f), new Vector3(0.9f, 2.6f, 0.9f), tawny);
        }
        Cube("Tail", t, new Vector3(0f, 0.6f, -5.3f), new Vector3(0.5f, 0.5f, 4f), tawny, new Vector3(-12f, 0f, 0f));
        Cube("Tuft", t, new Vector3(0f, 1.1f, -7.4f), new Vector3(1.1f, 1.1f, 1.4f), feather);
        var lw = Pivot(t, new Vector3(1.4f, 1.2f, 0.8f)); var rw = Pivot(t, new Vector3(-1.4f, 1.2f, 0.8f));
        foreach (var (w, s) in new[] { (lw, 1f), (rw, -1f) })
        {
            Cube("Feathers", w, new Vector3(s * 4.6f, 0f, 0f), new Vector3(9f, 0.4f, 4.6f), feather);
            Cube("Tip", w, new Vector3(s * 10.6f, 0f, -0.6f), new Vector3(5f, 0.3f, 3f), white);
        }
        // 기사: 안장·갑옷·투구(깃털)·창·방패
        Cube("Saddle", t, new Vector3(0f, 1.8f, 0.5f), new Vector3(2.6f, 0.6f, 2.4f), RedCloth);
        Cube("Knight", t, new Vector3(0f, 3.0f, 0.5f), new Vector3(1.4f, 1.8f, 1.0f), armor);
        Cube("Helmet", t, new Vector3(0f, 4.4f, 0.5f), new Vector3(0.9f, 1.0f, 0.9f), armor);
        Cube("Plume", t, new Vector3(0f, 5.2f, 0.4f), new Vector3(0.2f, 0.8f, 1.0f), RedCloth);
        Cube("Lance", t, new Vector3(0.9f, 3.2f, 4.0f), new Vector3(0.3f, 0.3f, 9f), WoodLight);
        Cube("LanceTip", t, new Vector3(0.9f, 3.2f, 8.8f), new Vector3(0.5f, 0.5f, 1.2f), armor);
        Cube("Shield", t, new Vector3(-0.9f, 3.0f, 0.5f), new Vector3(0.2f, 1.6f, 1.2f), BlueCloth);

        var ai = root.AddComponent<HeliAI>();
        ai.mainRotors = new Transform[0]; ai.tailRotors = new Transform[0];
        ai.projectile = CombatFX.Kind.Javelin; ai.scoreLabel = "그리폰 기사";
        ai.fireInterval = 1.3f; ai.rocketSpeed = 150f; ai.damage = 18f; ai.homing = 0f;
        ai.maxSpeed = 85f; ai.moveSpeedMultiplier = 1.7f; ai.maxHP = 35f; ai.contactRadius = 9f;
        var f = root.AddComponent<WingFlap>();
        f.leftWings = new[] { lw }; f.rightWings = new[] { rw }; f.rate = 1.6f; f.amplitude = 38f;
        return root;
    }

    // 와이번(s = 1, 늦은 헬기 대신)·드래곤(s = 2.2, 전투기 대신): 긴 목·꼬리, 박쥐 날개
    // 와이번: 크고 단단, 느리게 선회하며 끈질기게 따라오는 불덩이를 뱉음 / 드래곤: 거대, 머리 위를 지나가며 불을 뿜음 (몸에 닿으면 격추)
    static GameObject Drake(string name, float s, bool dragon)
    {
        var root = new GameObject(name);
        var t = root.transform;
        Material skin = dragon ? Mat("DragonRed", new Color(0.62f, 0.14f, 0.11f)) : Mat("WyvernGreen", new Color(0.30f, 0.46f, 0.30f));
        Material belly = dragon ? Mat("DragonBelly", new Color(0.88f, 0.66f, 0.34f)) : Mat("WyvernBelly", new Color(0.68f, 0.72f, 0.46f));
        Material membrane = dragon ? Mat("DragonWing", new Color(0.42f, 0.09f, 0.08f)) : Mat("WyvernWing", new Color(0.24f, 0.33f, 0.27f));
        Material horn = Mat("Horn", new Color(0.92f, 0.89f, 0.80f));
        Material eye = Mat("Eye", new Color(1f, 0.85f, 0.15f));
        Vector3 S(float x, float y, float z) => new Vector3(x, y, z) * s;
        Cube("Body", t, Vector3.zero, S(5f, 4.5f, 9f), skin);
        Cube("Belly", t, S(0f, -2.0f, 0f), S(4.6f, 1.2f, 8f), belly);
        Cube("Neck1", t, S(0f, 1.8f, 5.5f), S(2.4f, 2.4f, 3.4f), skin, new Vector3(-25f, 0f, 0f));
        Cube("Neck2", t, S(0f, 3.4f, 7.8f), S(2.1f, 2.1f, 3.2f), skin, new Vector3(-15f, 0f, 0f));
        Cube("Head", t, S(0f, 4.6f, 10.4f), S(2.4f, 2.0f, 4f), skin);
        Cube("Jaw", t, S(0f, 3.7f, 10.8f), S(2.2f, 0.8f, 3.2f), belly);
        foreach (var x in new[] { -0.7f, 0.7f })
        {
            Cube("Horn", t, S(x, 5.9f, 8.9f), S(0.4f, 0.4f, 2.6f), horn, new Vector3(-30f, 0f, 0f));
            Cube("Eye", t, S(x * 1.25f, 5.0f, 11.4f), S(0.35f, 0.35f, 0.35f), eye);
        }
        Cube("Tail1", t, S(0f, 0.4f, -6.5f), S(2.6f, 2.2f, 4.2f), skin);
        Cube("Tail2", t, S(0f, 0.7f, -10.2f), S(1.8f, 1.6f, 4f), skin);
        Cube("Tail3", t, S(0f, 1.0f, -13.8f), S(1.0f, 1.0f, 4f), skin);
        Cube("TailSpade", t, S(0f, 1.1f, -16.2f), S(2.6f, 0.4f, 2.2f), membrane, new Vector3(0f, 45f, 0f));
        int legs = dragon ? 2 : 1;
        for (int i = 0; i < legs; i++)
            foreach (var x in new[] { -1.7f, 1.7f })
            {
                float z = i == 0 ? -1.5f : 2.8f;
                Cube("Leg", t, S(x, -3.2f, z), S(1.3f, 3.6f, 1.3f), skin);
                Cube("Claw", t, S(x, -5.0f, z + 0.5f), S(1.4f, 0.6f, 1.8f), horn);
            }
        if (dragon) for (int i = 0; i < 6; i++) Cube("Spike", t, S(0f, 2.6f, 3.5f - i * 2.4f), S(0.5f, 1.4f, 1.2f), horn, new Vector3(-20f, 0f, 0f));
        var lw = Pivot(t, S(2.4f, 1.6f, 1.5f)); var rw = Pivot(t, S(-2.4f, 1.6f, 1.5f));
        foreach (var (w, sg) in new[] { (lw, 1f), (rw, -1f) })
        {
            Cube("Arm", w, S(sg * 6f, 0f, 0f), S(12f, 0.7f, 0.7f), skin);
            Cube("Membrane", w, S(sg * 6f, -0.1f, -3.4f), S(12f, 0.25f, 6.6f), membrane);
            Cube("Arm2", w, S(sg * 16f, 0f, -0.8f), S(8.5f, 0.5f, 0.5f), skin, new Vector3(0f, sg * -12f, 0f));
            Cube("Membrane2", w, S(sg * 16f, -0.1f, -3.8f), S(8.5f, 0.2f, 5.6f), membrane, new Vector3(0f, sg * -12f, 0f));
        }
        var flap = root.AddComponent<WingFlap>();
        flap.leftWings = new[] { lw }; flap.rightWings = new[] { rw };
        if (dragon)
        {
            var mouth = Empty("Mouth", t, S(0f, 3.9f, 12.8f));
            var jet = root.AddComponent<JetAI>();
            jet.speed = 150f; jet.turnRate = 42f; jet.passHeightAboveHead = 42f; jet.passDistance = 420f; jet.egressTime = 7f;
            jet.contactRadius = 16f; jet.maxHP = 220f; jet.scoreLabel = "드래곤"; // 머리 위로 높이 지나가며 불을 뿜음 (몸에 닿으면 격추)
            var b = root.AddComponent<DragonBreath>();
            b.mouth = mouth; b.range = 140f; b.dps = 30f;
            flap.rate = 0.55f; flap.amplitude = 28f;
            int layer = LayerMask.NameToLayer("Military");
            foreach (var x in root.GetComponentsInChildren<Transform>(true)) x.gameObject.layer = layer; // 전투기처럼
        }
        else
        {
            var ai = root.AddComponent<HeliAI>();
            ai.mainRotors = new Transform[0]; ai.tailRotors = new Transform[0];
            ai.projectile = CombatFX.Kind.Fireball; ai.scoreLabel = "와이번";
            ai.fireInterval = 3.6f; ai.rocketSpeed = 55f; ai.damage = 75f; ai.homing = 1.1f;
            ai.maxSpeed = 55f; ai.moveSpeedMultiplier = 0.9f; ai.maxHP = 130f; ai.contactRadius = 15f;
            flap.rate = 0.9f; flap.amplitude = 32f;
        }
        return root;
    }

    // 대마법사의 떠 있는 섬 (UFO 대신): 거꾸로 선 바위 섬 위에 탑·마법 수정·지팡이 든 마법사. 축소 마법(보라 빛줄기)을 쏨
    static GameObject MageIsland()
    {
        var root = new GameObject("MageIsland");
        var t = root.transform;
        Material grass = Mat("IslandGrass", new Color(0.45f, 0.63f, 0.33f)), rock = Mat("IslandRock", new Color(0.52f, 0.49f, 0.45f));
        Material purple = Mat("Arcane", new Color(0.42f, 0.18f, 0.62f)), crystal = Mat("Crystal", new Color(0.78f, 0.45f, 1f));
        Material skin = Mat("Skin", new Color(0.95f, 0.80f, 0.68f));
        Cube("Grass", t, new Vector3(0f, 0.3f, 0f), new Vector3(10f, 0.6f, 10f), grass);
        Cube("Rock1", t, new Vector3(0f, -0.75f, 0f), new Vector3(9f, 1.5f, 9f), rock);
        Cube("Rock2", t, new Vector3(0f, -2.25f, 0f), new Vector3(7f, 1.5f, 7f), rock, new Vector3(0f, 20f, 0f));
        Cube("Rock3", t, new Vector3(0f, -3.75f, 0f), new Vector3(4.6f, 1.5f, 4.6f), rock, new Vector3(0f, 45f, 0f));
        Cube("Rock4", t, new Vector3(0f, -5.1f, 0f), new Vector3(2f, 1.2f, 2f), rock, new Vector3(0f, 10f, 0f));
        // 탑
        Cube("Tower", t, new Vector3(2f, 2.8f, 1.5f), new Vector3(2.2f, 5f, 2.2f), Stone);
        Cube("Roof1", t, new Vector3(2f, 5.6f, 1.5f), new Vector3(2.8f, 0.6f, 2.8f), purple);
        Cube("Roof2", t, new Vector3(2f, 6.2f, 1.5f), new Vector3(2.0f, 0.8f, 2.0f), purple);
        Cube("Roof3", t, new Vector3(2f, 7.0f, 1.5f), new Vector3(1.2f, 0.8f, 1.2f), purple);
        Cube("Roof4", t, new Vector3(2f, 7.9f, 1.5f), new Vector3(0.5f, 1.0f, 0.5f), purple);
        // 마법 수정 (다이아몬드 모양)
        Cube("Crystal", t, new Vector3(-2.2f, 2.6f, -1.5f), new Vector3(1.6f, 1.6f, 1.6f), crystal, new Vector3(45f, 0f, 35.26f));
        Cube("CrystalBase", t, new Vector3(-2.2f, 0.9f, -1.5f), new Vector3(1.6f, 0.6f, 1.6f), Stone);
        // 마법사: 로브·얼굴·고깔모자·지팡이
        Cube("Robe", t, new Vector3(-0.5f, 1.6f, 1.8f), new Vector3(1.0f, 2.0f, 1.0f), purple);
        Cube("Face", t, new Vector3(-0.5f, 2.9f, 1.8f), new Vector3(0.6f, 0.6f, 0.6f), skin);
        Cube("Brim", t, new Vector3(-0.5f, 3.25f, 1.8f), new Vector3(1.3f, 0.15f, 1.3f), purple);
        Cube("Hat1", t, new Vector3(-0.5f, 3.6f, 1.8f), new Vector3(0.7f, 0.6f, 0.7f), purple);
        Cube("Hat2", t, new Vector3(-0.5f, 4.1f, 1.8f), new Vector3(0.35f, 0.6f, 0.35f), purple, new Vector3(0f, 0f, 12f));
        Cube("Staff", t, new Vector3(0.25f, 1.9f, 1.8f), new Vector3(0.15f, 3f, 0.15f), WoodDark);
        Cube("Orb", t, new Vector3(0.25f, 3.5f, 1.8f), new Vector3(0.45f, 0.45f, 0.45f), crystal, new Vector3(45f, 0f, 35.26f));
        // 작은 나무 둘
        foreach (var p in new[] { new Vector3(-3.4f, 0f, 2.8f), new Vector3(3.2f, 0f, -3f) })
        {
            Cube("Trunk", t, p + new Vector3(0f, 1.1f, 0f), new Vector3(0.35f, 1.6f, 0.35f), WoodDark);
            Cube("Leaves", t, p + new Vector3(0f, 2.4f, 0f), new Vector3(1.6f, 1.4f, 1.6f), grass, new Vector3(0f, 30f, 0f));
        }
        return root;
    }

    // ───────────── 마을 밖 (구매 에셋 LowPolyNature 4 Seasons · LowPolyFarm) ─────────────
    const string Nat = "Assets/LowPolyNature (4 Seasons)/Prefabs/";
    const string Farm = "Assets/LowPolyFarm/Prefabs/";

    static GameObject[] Prefabs(params string[] paths)
    {
        var list = new List<GameObject>();
        foreach (var p in paths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p + ".prefab");
            if (go) list.Add(go);
        }
        return list.ToArray();
    }

    static string[] Range(string prefix, int from, int to)
    {
        var a = new List<string>();
        for (int i = from; i <= to; i++) a.Add($"{prefix}{i:00}");
        return a.ToArray();
    }

    static string[] Join(params string[][] parts) { var a = new List<string>(); foreach (var p in parts) a.AddRange(p); return a.ToArray(); }

    public static void SetupWilderness()
    {
        var w = Object.FindObjectOfType<Wilderness>();
        if (!w) w = new GameObject("Wilderness").AddComponent<Wilderness>();
        w.city = Object.FindObjectOfType<CityGenerator>();
        w.trees = Prefabs(Join(Range(Nat + "N_Summer/Summer_Tree_", 1, 15), Range(Farm + "Nature/Tree_", 1, 5), Range(Farm + "Plants/AppleTree_", 1, 4)));
        w.bigTrees = Prefabs(Nat + "N_Summer/Summer_Tree_05", Nat + "N_Summer/Summer_Tree_10", Nat + "N_Summer/Summer_Tree_11", Nat + "N_Summer/Summer_Tree_12",
            Nat + "N_Summer/Summer_Tree_13", Nat + "N_Summer/Summer_Tree_14", Farm + "Nature/Tree_03", Farm + "Nature/Tree_05", Farm + "Plants/AppleTree_03");
        w.bushes = Prefabs(Join(Range(Nat + "N_Summer/Summer_Plant_", 1, 3), Range(Farm + "Nature/Plant_", 1, 2)));
        w.grass = Prefabs(Join(Range(Nat + "N_Summer/Summer_Grass_", 1, 5), Range(Nat + "N_Summer/Summer_Flower_", 5, 9), Range(Farm + "Nature/Flower_", 1, 5), new[] { Farm + "Nature/Reeds_01" }, Range(Nat + "N_Summer/Summer_Mushroom_", 1, 3)));
        w.rocks = Prefabs(Join(Range(Nat + "Rocks/Rock_", 1, 25), Range(Farm + "Nature/Rock_", 1, 5)));
        w.logs = Prefabs(Join(Range(Nat + "N_Summer/Summer_Log_", 1, 3), Range(Nat + "N_Summer/Summer_Stump_", 1, 2), new[] { Farm + "Nature/Log_01", Farm + "Nature/Stump_01", Farm + "Props/Logs_01" }));
        w.mountains = Prefabs(Join(Range(Nat + "Terrain/Mountain_", 1, 5), Range(Farm + "Landscape/Mountain_", 1, 3)));
        w.mounds = Prefabs(Join(Range(Farm + "Landscape/Hill_", 1, 8), Range(Nat + "Terrain/Hill_", 1, 5)));
        w.crops = Prefabs(Join(Range(Farm + "Plants/CornPlant_", 1, 4), Range(Farm + "Plants/Sunflower_", 1, 2), Range(Farm + "Plants/Cabbage_", 1, 4), Range(Farm + "Plants/Pumpkin_", 1, 3), Range(Farm + "Plants/TomatoPlant_", 1, 2), Range(Farm + "Plants/PepperPlant_", 1, 4), new[] { Farm + "Plants/Millet_02" }));
        w.hay = Prefabs(Farm + "Props/Hay_01", Farm + "Props/Hay_02");
        w.fences = Prefabs(Range(Farm + "Props/Fence_", 1, 6));
        w.farmProps = Prefabs(Join(Range(Farm + "Props/Barrel_", 1, 4), Range(Farm + "Props/Wheelbarrow_", 1, 3), Range(Farm + "Props/Bag_", 1, 5), new[] { Farm + "Props/Box_07", Farm + "Props/BeeHive_01", Farm + "Props/Bench_01", Farm + "Props/Doghouse_01", Farm + "Props/Logs_01" }));
        w.tents = Prefabs(Range(Nat + "Props/Tent_", 1, 3));
        w.scarecrow = AssetDatabase.LoadAssetAtPath<GameObject>(Farm + "Props/Scarecrow_01.prefab");
        w.well = AssetDatabase.LoadAssetAtPath<GameObject>(Farm + "Props/Well_01.prefab");
        w.windmill = AssetDatabase.LoadAssetAtPath<GameObject>(Farm + "Buildings/Windmill_02.prefab");
        w.campfire = AssetDatabase.LoadAssetAtPath<GameObject>(Nat + "Props/Fire_01.prefab");
        w.creatureMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Prop_WoodLight.mat");
        EditorUtility.SetDirty(w);

        // 구매 에셋 재질: GPU 인스턴싱 켜고 반짝임 끔 (만화풍 화면에 맞게)
        foreach (var dir in new[] { "Assets/LowPolyNature (4 Seasons)/Materials", "Assets/LowPolyFarm/Materials" })
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { dir }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                m.enableInstancing = true;
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.05f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                EditorUtility.SetDirty(m);
            }
        Debug.Log($"[MedievalBuilder] 마을 밖: 나무 {w.trees.Length} · 수풀 {w.bushes.Length} · 풀꽃 {w.grass.Length} · 바위 {w.rocks.Length} · 산 {w.mountains.Length} · 작물 {w.crops.Length}");
    }

    // ───────────── 씬 설정 ─────────────
    [MenuItem("Tools/Medieval/Setup Scene")]
    public static void SetupScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        GameObject P(string n) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{n}.prefab");

        // 중세 표시 (공용 스크립트의 모양 전환)
        var theme = Object.FindObjectOfType<MedievalTheme>();
        if (!theme) theme = new GameObject("MedievalTheme").AddComponent<MedievalTheme>();

        var ms = Object.FindObjectOfType<MilitarySpawner>();
        if (ms)
        {
            // 현대와 같은 순서·조건으로 등장: 경비대(처음) → 투석기(탱크) → 그리폰 기사(헬기) → 트레뷰셋(중전차) → 와이번(늦은 헬기)·드래곤(전투기)
            ms.tankPrefab = P("Catapult");
            ms.heavyTankPrefab = P("Trebuchet");
            ms.heliPrefabs = new[] { P("GriffinKnight"), P("Wyvern") }; ms.lateHeliPrefab = P("Wyvern"); ms.maxHelis = 7; ms.helisPerLevel = 0.15f;
            ms.jetPrefabs = new[] { P("Dragon") }; ms.maxJets = 2; ms.jetsPerLevel = 0.05f; // 드래곤은 거대해서 많아야 둘
            ms.policeCarPrefab = P("GuardWagon");
            ms.maxTanks = 12; ms.tanksPerLevel = 0.25f;
            // 궁수를 태우고 달리면서 쏘는 병력 마차
            var tw = ms.GetComponent<TroopWagonSpawner>();
            if (!tw) tw = ms.gameObject.AddComponent<TroopWagonSpawner>();
            tw.wagonPrefab = P("TroopWagon");
            EditorUtility.SetDirty(ms);
        }
        var tm = Object.FindObjectOfType<TrafficManager>();
        if (tm)
        {
            tm.carPrefabs = new[] { P("Cart_Hay"), P("Cart_Barrels"), P("Cart_Sacks"), P("Cart_Covered"), P("Cart_Hay"), P("Carriage") };
            EditorUtility.SetDirty(tm);
        }
        var ufo = Object.FindObjectOfType<UfoEnemy>(true);
        if (ufo) { ufo.enabled = true; ufo.ufoPrefab = P("MageIsland"); ufo.spinSpeed = 10f; EditorUtility.SetDirty(ufo); } // UFO 자리: 대마법사의 떠 있는 섬 (축소 마법)
        var tall = Object.FindObjectOfType<TallBuildings>();
        // 초고층으로 키우지 않음 (성·탑·대성당이 높은 건물). 지붕까지 잰 높이가 거인 키의 0.6배 이상이면 부술 수 없음
        if (tall) { tall.maxRatio = 0f; tall.heightBoost = 1f; tall.measureWithRoof = true; tall.breakableHeightRatio = 0.6f; EditorUtility.SetDirty(tall); }
        // 성벽 위 궁수·투석기
        if (!Object.FindObjectOfType<WallDefense>()) new GameObject("WallDefense").AddComponent<WallDefense>();
        var fx = Object.FindObjectOfType<CombatFX>();
        if (fx) { fx.boulderMaterial = BoulderMaterial(); fx.javelinMaterial = WoodLight; EditorUtility.SetDirty(fx); }
        var fp = Object.FindObjectOfType<Flagpoles>();
        if (fp) { fp.count = 0; EditorUtility.SetDirty(fp); }
        // 따뜻한 햇빛
        foreach (var l in Object.FindObjectsOfType<Light>())
            if (l.type == LightType.Directional) { l.color = new Color(1f, 0.95f, 0.86f); EditorUtility.SetDirty(l); }

        SetupWilderness();

        // 빌드 목록에 추가
        string path = scene.path;
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == path)) { list.Add(new EditorBuildSettingsScene(path, true)); EditorBuildSettings.scenes = list.ToArray(); }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[MedievalBuilder] 씬 설정 완료: " + path);
    }
}
