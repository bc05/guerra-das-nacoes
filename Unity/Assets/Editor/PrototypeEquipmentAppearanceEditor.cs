using System;
using System.Collections.Generic;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Splits triangle groups only: source UVs, normals, skin weights, bind poses and
/// sword markers remain intact. No runtime mesh access or readable FBX is needed.
/// </summary>
public static class PrototypeEquipmentAppearanceEditor
{
    private const string ModelPath = "Assets/Art/Cavaleiro.fbx";
    private const string OutputDirectory = "Assets/Art/Equipment";
    private const string ArmourPath = OutputDirectory + "/KnightArmorParts.asset";
    private const string ShieldPath = OutputDirectory + "/KnightShield.asset";
    private const string SkinDirectory = "Assets/Art/ThirdParty/BaseHero";

    public static void Configure(GameObject player)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        SkinnedMeshRenderer source = FindRenderer(model, "KnightArmor");
        SkinnedMeshRenderer armour = FindRenderer(player, "KnightArmor");
        SkinnedMeshRenderer sword = FindRenderer(player, "KnightSword");
        if (source == null || armour == null || sword == null)
            throw new BuildFailedException("Cavaleiro incompleto: faltam as malhas KnightArmor/KnightSword.");
        if (!AssetDatabase.IsValidFolder(OutputDirectory))
            AssetDatabase.CreateFolder("Assets/Art", "Equipment");

        Partition(source, out Mesh armourMesh, out Mesh shieldMesh);
        armourMesh = SaveMesh(armourMesh, ArmourPath);
        shieldMesh = SaveMesh(shieldMesh, ShieldPath);
        Material material = source.sharedMaterial;
        armour.sharedMesh = armourMesh;
        armour.sharedMaterials = Enumerable.Repeat(material, PrototypeEquipmentAppearance.ArmourSlots.Length).ToArray();
        Transform shieldNode = armour.transform.Find("KnightShield");
        if (shieldNode == null)
        {
            var shieldObject = new GameObject("KnightShield");
            shieldObject.layer = armour.gameObject.layer;
            shieldNode = shieldObject.transform;
            shieldNode.SetParent(armour.transform, false);
        }
        SkinnedMeshRenderer shield = shieldNode.GetComponent<SkinnedMeshRenderer>();
        if (shield == null)
            shield = shieldNode.gameObject.AddComponent<SkinnedMeshRenderer>();
        shield.sharedMesh = shieldMesh;
        shield.sharedMaterial = material;
        shield.bones = armour.bones;
        shield.rootBone = armour.rootBone;
        shield.localBounds = armour.localBounds;
        shield.quality = armour.quality;
        shield.updateWhenOffscreen = armour.updateWhenOffscreen;
        shield.shadowCastingMode = armour.shadowCastingMode;
        shield.receiveShadows = armour.receiveShadows;
        shield.enabled = true;
        // Each armour piece has its own renderer. Removing it no longer draws the
        // armoured silhouette with a different colour; the fitted cloth body remains.
        var pieces = new SkinnedMeshRenderer[PrototypeEquipmentAppearance.ArmourSlots.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            Mesh pieceMesh = UnityEngine.Object.Instantiate(armourMesh);
            pieceMesh.name = "KnightArmour_" + PrototypeEquipmentAppearance.ArmourSlots[i];
            int[] indices = armourMesh.GetTriangles(i);
            pieceMesh.subMeshCount = 1;
            pieceMesh.SetTriangles(indices, 0, false);
            pieceMesh = SaveMesh(pieceMesh, OutputDirectory + "/" + pieceMesh.name + ".asset");
            pieces[i] = ConfigureRenderer(armour, pieceMesh.name, pieceMesh, new[] { material }, armour.bones);
        }
        GameObject baseModel = AssetDatabase.LoadAssetAtPath<GameObject>(OutputDirectory + "/BaseHero.fbx");
        if (baseModel == null) throw new BuildFailedException("Falta a base anatômica vestida BaseHero.fbx.");
        ConfigureBaseSkinTexture();
        SkinnedMeshRenderer baseBody = ConfigureBaseRenderer(baseModel, armour, "BaseHeroBody");
        SkinnedMeshRenderer baseHead = ConfigureBaseRenderer(baseModel, armour, "BaseHeroHead");
        SkinnedMeshRenderer[] baseRegions = PartitionBaseBody(baseBody);
        string[] accessoryNames = { "BaseHeroBelt", "BaseHeroNecklace", "BaseHeroRingLeft", "BaseHeroRingRight" };
        SkinnedMeshRenderer[] accessories = accessoryNames.Select(name => ConfigureBaseRenderer(baseModel, armour, name)).ToArray();
        Transform visualRoot = armour.GetComponentInParent<Animator>().transform;
        PrototypeEquipmentVisual visual = visualRoot.GetComponent<PrototypeEquipmentVisual>();
        if (visual == null) visual = visualRoot.gameObject.AddComponent<PrototypeEquipmentVisual>();
        visual.Configure(armour, pieces, baseBody, baseHead, sword, shield, accessories);
        visual.ConfigureBaseRegions(baseRegions);
        PrototypeEquipmentAppearance appearance = player.GetComponent<PrototypeEquipmentAppearance>();
        if (appearance == null) appearance = player.AddComponent<PrototypeEquipmentAppearance>();
        appearance.Configure(visual);
        visual.ApplyEquipmentState(new PlayerProgression().Equipment);
        Validate(player);
        Debug.Log("[EQUIPMENT VISUAL PASS] Seis peças removíveis, corpo anatômico vestido, cabeça, quatro acessórios e rig original preservados.");
    }

    private static SkinnedMeshRenderer ConfigureRenderer(SkinnedMeshRenderer reference, string name,
        Mesh mesh, Material[] materials, Transform[] bones)
    {
        Transform node = reference.transform.Find(name);
        if (node == null)
        {
            var gameObject = new GameObject(name); gameObject.layer = reference.gameObject.layer;
            node = gameObject.transform; node.SetParent(reference.transform, false);
        }
        SkinnedMeshRenderer renderer = node.GetComponent<SkinnedMeshRenderer>();
        if (renderer == null) renderer = node.gameObject.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = mesh; renderer.sharedMaterials = materials; renderer.bones = bones;
        renderer.rootBone = reference.rootBone; renderer.localBounds = reference.localBounds;
        renderer.quality = reference.quality; renderer.updateWhenOffscreen = reference.updateWhenOffscreen;
        renderer.shadowCastingMode = reference.shadowCastingMode; renderer.receiveShadows = reference.receiveShadows;
        renderer.enabled = true;
        return renderer;
    }

    private static SkinnedMeshRenderer ConfigureBaseRenderer(GameObject baseModel,
        SkinnedMeshRenderer armour, string name)
    {
        SkinnedMeshRenderer source = FindRenderer(baseModel, name);
        if (source == null) throw new BuildFailedException("Base anatômica incompleta: " + name);
        var targetBones = armour.bones.ToDictionary(bone => bone.name);
        Transform[] mapped = source.bones.Select(bone => targetBones.TryGetValue(bone.name, out Transform target)
            ? target : throw new BuildFailedException("Osso não encontrado: " + bone.name)).ToArray();
        Material[] materials = source.sharedMaterials.Select(material => PrepareBaseMaterial(material.name)).ToArray();
        return ConfigureRenderer(armour, name, source.sharedMesh, materials, mapped);
    }

    private static SkinnedMeshRenderer[] PartitionBaseBody(SkinnedMeshRenderer body)
    {
        Mesh original = body.sharedMesh;
        BoneWeight[] weights = original.boneWeights;
        var groups = Enumerable.Range(0, PrototypeEquipmentAppearance.ArmourSlots.Length)
            .Select(_ => Enumerable.Range(0, original.subMeshCount).Select(__ => new List<int>()).ToArray()).ToArray();
        for (int materialIndex = 0; materialIndex < original.subMeshCount; materialIndex++)
        {
            int[] indices = original.GetTriangles(materialIndex);
            for (int t = 0; t < indices.Length; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                int first = PartIndex(body.bones[DominantBone(weights[a])].name);
                int second = PartIndex(body.bones[DominantBone(weights[b])].name);
                int third = PartIndex(body.bones[DominantBone(weights[c])].name);
                int part = second == third ? second : first;
                if (part == 0) part = 2; // clothing collar belongs to Chest; Head has genuine face/hair.
                groups[part][materialIndex].Add(a); groups[part][materialIndex].Add(b); groups[part][materialIndex].Add(c);
            }
        }
        var regions = new SkinnedMeshRenderer[PrototypeEquipmentAppearance.ArmourSlots.Length];
        for (int part = 1; part < regions.Length; part++)
        {
            Mesh mesh = UnityEngine.Object.Instantiate(original);
            mesh.name = "BaseHero_" + PrototypeEquipmentAppearance.ArmourSlots[part];
            mesh.subMeshCount = original.subMeshCount;
            for (int i = 0; i < original.subMeshCount; i++) mesh.SetTriangles(groups[part][i], i, false);
            mesh = SaveMesh(mesh, OutputDirectory + "/" + mesh.name + ".asset");
            regions[part] = ConfigureRenderer(body, mesh.name, mesh, body.sharedMaterials, body.bones);
        }
        return regions;
    }

    private static Material PrepareBaseMaterial(string name)
    {
        string cleanName = name.Replace(" (Instance)", "");
        string path = OutputDirectory + "/MAT_" + cleanName + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard")); material.name = "MAT_" + cleanName;
            AssetDatabase.CreateAsset(material, path);
        }
        Color colour; float smoothness = .12f; float metal = 0f;
        switch (cleanName)
        {
            case "BaseSkin": colour = Color.white; smoothness = .30f; break;
            case "BaseLinen": colour = new Color(.22f, .205f, .17f); break;
            case "BaseTrousers": colour = new Color(.105f, .12f, .13f); break;
            case "BaseLeather": colour = new Color(.105f, .064f, .038f); smoothness = .2f; break;
            case "BaseHair": colour = new Color(.055f, .032f, .021f); break;
            case "BaseEye": colour = new Color(.56f, .55f, .51f); smoothness = .5f; break;
            case "BaseIris": colour = new Color(.055f, .075f, .045f); smoothness = .45f; break;
            case "BaseAccessoryMetal": colour = new Color(.36f, .30f, .18f); smoothness = .5f; metal = .8f; break;
            default: throw new BuildFailedException("Material desconhecido na base: " + cleanName);
        }
        material.color = colour;
        material.SetFloat("_Mode", 0f);
        material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        material.SetInt("_ZWrite", 1); material.renderQueue = -1;
        material.SetOverrideTag("RenderType", "Opaque");
        material.SetFloat("_Glossiness", smoothness); material.SetFloat("_Metallic", metal);
        if (cleanName == "BaseSkin")
        {
            // Head and hands preserve the original MakeHuman mh08 UVs. The skin
            // atlas already contains their colour, so multiplying a brown tint loses detail.
            material.SetTexture("_MainTex", RequireSkinMap("BaseSkin_Albedo.png"));
            material.SetTexture("_BumpMap", null);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureScale("_BumpMap", Vector2.one);
            material.SetTexture("_MetallicGlossMap", null);
            material.SetFloat("_BumpScale", 0f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            // This neutral system skin has no authored normal map. Do not keep
            // relief from another skin or manufacture a normal from colour/shadows.
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_METALLICGLOSSMAP");
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            material.enableInstancing = true;
        }
        EditorUtility.SetDirty(material); return material;
    }

    private static void ConfigureBaseSkinTexture()
    {
        string path = SkinDirectory + "/BaseSkin_Albedo.png";
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new BuildFailedException("Textura de pele MakeHuman ausente: " + path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = true;
        importer.isReadable = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8;
        importer.mipMapBias = 0f;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static Texture2D RequireSkinMap(string name)
    {
        string path = SkinDirectory + "/" + name;
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture == null) throw new BuildFailedException("Mapa de pele MakeHuman não importado: " + path);
        return texture;
    }

    public static void Validate(GameObject player)
    {
        PrototypeEquipmentAppearance appearance = player.GetComponent<PrototypeEquipmentAppearance>();
        if (appearance == null || !appearance.IsConfigured)
            throw new BuildFailedException("A aparência dos equipamentos não foi configurada.");
        SkinnedMeshRenderer source = FindRenderer(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), "KnightArmor");
        Mesh body = appearance.ArmourRenderer.sharedMesh;
        Mesh shield = appearance.ShieldRenderer.sharedMesh;
        Mesh original = source.sharedMesh;
        if (body.vertexCount != original.vertexCount || shield.vertexCount != original.vertexCount ||
            body.bindposes.Length != original.bindposes.Length || shield.bindposes.Length != original.bindposes.Length)
            throw new BuildFailedException("A divisão dos equipamentos alterou vértices ou bind poses do cavaleiro.");
        int originalIndices = original.triangles.Length;
        int assignedIndices = shield.triangles.Length;
        for (int index = 0; index < body.subMeshCount; index++)
        {
            int count = (int)body.GetIndexCount(index);
            if (count == 0)
                throw new BuildFailedException("Uma peça da armadura ficou sem geometria: " + PrototypeEquipmentAppearance.ArmourSlots[index]);
            assignedIndices += count;
        }
        if (assignedIndices != originalIndices)
            throw new BuildFailedException("A divisão dos equipamentos perdeu ou duplicou triângulos.");
        var uniqueTriangles = new HashSet<string>();
        foreach (int subMesh in Enumerable.Range(0, body.subMeshCount))
            AssertUnique(body.GetTriangles(subMesh), uniqueTriangles);
        AssertUnique(shield.triangles, uniqueTriangles);
        var sourceTriangles = new HashSet<string>();
        AssertUnique(original.triangles, sourceTriangles);
        if (!uniqueTriangles.SetEquals(sourceTriangles))
            throw new BuildFailedException("A divisão dos equipamentos mudou a topologia original.");
        BoneWeight[] weights = shield.boneWeights;
        foreach (int vertex in shield.triangles.Distinct())
        {
            BoneWeight weight = weights[vertex];
            if (source.bones[DominantBone(weight)].name != "Hand.L")
                throw new BuildFailedException("O escudo isolado contém geometria de outra parte do corpo.");
        }
        for (int i = 0; i < PrototypeEquipmentAppearance.ArmourSlots.Length; i++)
        {
            SkinnedMeshRenderer piece = appearance.GetArmourPiece(PrototypeEquipmentAppearance.ArmourSlots[i]);
            if (piece == null || piece.sharedMesh.subMeshCount != 1 ||
                !piece.sharedMesh.triangles.SequenceEqual(body.GetTriangles(i)))
                throw new BuildFailedException("Peça removível difere da partição: " + PrototypeEquipmentAppearance.ArmourSlots[i]);
        }
        if (appearance.ArmourRenderer.enabled || appearance.BaseBodyRenderer.sharedMesh.vertexCount < 4000 ||
            appearance.BaseHeadRenderer.sharedMesh.vertexCount < 3000)
            throw new BuildFailedException("A base anatômica ou o conjunto modular está incompleto.");
        foreach (EquipmentSlot slot in PrototypeEquipmentVisual.AccessorySlots)
            if (appearance.GetAccessory(slot) == null || appearance.GetAccessory(slot).sharedMesh.vertexCount == 0)
                throw new BuildFailedException("Acessório visual incompleto: " + slot);
        SkinnedMeshRenderer originalSword = FindRenderer(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), "KnightSword");
        if (appearance.SwordRenderer.name != "KnightSword" ||
            appearance.SwordRenderer.sharedMesh != originalSword.sharedMesh)
            throw new BuildFailedException("A espada original do cavaleiro não foi preservada.");
    }

    private static void AssertUnique(int[] indices, HashSet<string> destination)
    {
        for (int i = 0; i < indices.Length; i += 3)
        {
            string key = indices[i] + ":" + indices[i + 1] + ":" + indices[i + 2];
            if (!destination.Add(key))
                throw new BuildFailedException("Triângulo duplicado na preparação dos equipamentos.");
        }
    }

    private static Mesh SaveMesh(Mesh mesh, string path)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static SkinnedMeshRenderer FindRenderer(GameObject root, string name)
    {
        return root == null ? null : root.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == name);
    }

    private static void Partition(SkinnedMeshRenderer source, out Mesh body, out Mesh shield)
    {
        Mesh original = source.sharedMesh;
        Vector3[] positions = original.vertices;
        BoneWeight[] weights = original.boneWeights;
        int[] triangles = original.triangles;
        if (positions.Length == 0 || positions.Length != weights.Length)
            throw new BuildFailedException("A malha do cavaleiro não possui pesos legíveis para preparar equipamentos.");
        var components = new Components(positions, triangles, original.bounds.size.magnitude * 0.000001f);
        var componentSizes = new Dictionary<int, int>();
        var componentAllShield = new Dictionary<int, bool>();
        for (int i = 0; i < positions.Length; i++)
        {
            int component = components.Find(i);
            string bone = source.bones[DominantBone(weights[i])].name;
            componentSizes.TryGetValue(component, out int count);
            componentSizes[component] = count + 1;
            componentAllShield.TryGetValue(component, out bool allShield);
            componentAllShield[component] = (count == 0 || allShield) && bone == "Hand.L";
        }
        int[] candidates = componentSizes.Keys.Where(key => componentSizes[key] >= 300 && componentAllShield[key]).ToArray();
        if (candidates.Length != 1)
            throw new BuildFailedException("Não foi possível isolar a ilha do escudo sem remover geometria do corpo.");
        int shieldComponent = candidates[0];
        var parts = Enumerable.Range(0, PrototypeEquipmentAppearance.ArmourSlots.Length).Select(_ => new List<int>()).ToArray();
        var shieldTriangles = new List<int>();
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            if (components.Find(a) == shieldComponent)
            {
                shieldTriangles.Add(a); shieldTriangles.Add(b); shieldTriangles.Add(c);
                continue;
            }
            int first = PartIndex(source.bones[DominantBone(weights[a])].name);
            int second = PartIndex(source.bones[DominantBone(weights[b])].name);
            int third = PartIndex(source.bones[DominantBone(weights[c])].name);
            int part = second == third ? second : first;
            parts[part].Add(a); parts[part].Add(b); parts[part].Add(c);
        }
        body = UnityEngine.Object.Instantiate(original);
        body.name = "KnightArmorParts";
        body.subMeshCount = parts.Length;
        for (int i = 0; i < parts.Length; i++)
            body.SetTriangles(parts[i], i, false);
        shield = UnityEngine.Object.Instantiate(original);
        shield.name = "KnightShield";
        shield.subMeshCount = 1;
        shield.SetTriangles(shieldTriangles, 0, false);
        Debug.Log($"[EQUIPMENT PARTS] source={triangles.Length / 3} shield={shieldTriangles.Count / 3} body={string.Join(",", parts.Select(p => p.Count / 3))} shieldVertices={componentSizes[shieldComponent]}");
    }

    private static int DominantBone(BoneWeight weight)
    {
        int index = weight.boneIndex0;
        float maximum = weight.weight0;
        if (weight.weight1 > maximum) { maximum = weight.weight1; index = weight.boneIndex1; }
        if (weight.weight2 > maximum) { maximum = weight.weight2; index = weight.boneIndex2; }
        if (weight.weight3 > maximum) index = weight.boneIndex3;
        return index;
    }

    private static int PartIndex(string bone)
    {
        if (bone == "Head") return 0;
        if (bone.StartsWith("UpperArm.", StringComparison.Ordinal)) return 1;
        if (bone.StartsWith("Forearm.", StringComparison.Ordinal) || bone.StartsWith("Hand.", StringComparison.Ordinal)) return 3;
        if (bone.StartsWith("Thigh.", StringComparison.Ordinal) || bone == "Pelvis") return 4;
        if (bone.StartsWith("Shin.", StringComparison.Ordinal) || bone.StartsWith("Foot.", StringComparison.Ordinal)) return 5;
        return 2;
    }

    private sealed class Components
    {
        private readonly int[] parents;

        public Components(Vector3[] vertices, int[] triangles, float tolerance)
        {
            parents = Enumerable.Range(0, vertices.Length).ToArray();
            var positions = new Dictionary<Vector3Int, int>();
            float quantum = Mathf.Max(0.00000001f, tolerance);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i] / quantum;
                var key = new Vector3Int(Mathf.RoundToInt(vertex.x), Mathf.RoundToInt(vertex.y), Mathf.RoundToInt(vertex.z));
                if (positions.TryGetValue(key, out int duplicate)) Join(i, duplicate);
                else positions[key] = i;
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Join(triangles[i], triangles[i + 1]);
                Join(triangles[i], triangles[i + 2]);
            }
        }

        public int Find(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }

        private void Join(int first, int second) { parents[Find(first)] = Find(second); }
    }
}
