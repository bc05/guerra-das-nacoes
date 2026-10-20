using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Imports the CC0 Poly Haven surfaces and scanned rocks used by the prototype.</summary>
public static class ThirdPartyEnvironmentSetup
{
    private const string Root = "Assets/Art/ThirdParty/Environment";
    private const string Materials = "Assets/Materials/ThirdPartyEnvironment";
    private const string EnvironmentModel = "Assets/Art/Cenario.fbx";
    private const string SkyPath = Root + "/KloofendalSky.hdr";
    private const string PropsName = "Rochas escaneadas — Poly Haven CC0";

    private static readonly string[] SurfaceIds =
    {
        "medieval_wall_02", "cobblestone_floor_07", "forest_ground_04", "rock_moss_set_01"
    };

    public static void ConfigureMaterialsAndAssets()
    {
        Directory.CreateDirectory(Materials);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (string surface in SurfaceIds)
            foreach (string channel in new[] { "Albedo", "Normal", "AO", "MetallicSmoothness" })
                ConfigureTexture(Root + "/" + surface + "/" + channel + ".png", channel);

        // Cenario.fbx uses one UV unit per metre. These repeats preserve the scans' physical sizes.
        // Each scan has authored roughness and relief. Keep the wall/soil dry while
        // allowing worn paving and mossy rock to retain a broader range of reflections.
        Material wall = CreateSurface("MuroEscaneado", SurfaceIds[0], 1f / 3.5f, 1f, 1f, 0.85f);
        Material paving = CreateSurface("PisoEscaneado", SurfaceIds[1], 1f / 2f, 0.85f, 0.9f, 0.8f);
        Material earth = CreateSurface("TerraEscaneada", SurfaceIds[2], 1f / 3.2f, 1f, 0.7f, 0.85f);
        Material rock = CreateSurface("RochaEscaneada", SurfaceIds[3], 1f, 0.65f, 0.7f, 0.8f);

        ModelImporter environment = RequireImporter<ModelImporter>(EnvironmentModel);
        Remap(environment, "MAT_Pedra", wall);
        Remap(environment, "MAT_PedraClara", wall);
        Remap(environment, "MAT_PedraEscura", wall);
        Remap(environment, "MAT_Caminho", paving);
        Remap(environment, "MAT_Chao", earth);
        environment.SaveAndReimport();

        for (int index = 1; index <= 6; index++)
        {
            ModelImporter importer = RequireImporter<ModelImporter>(RockPath(index));
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.isReadable = false;
            Remap(importer, "rock_moss_set_01", rock);
            importer.SaveAndReimport();
        }

        TextureImporter skyImporter = RequireImporter<TextureImporter>(SkyPath);
        skyImporter.textureType = TextureImporterType.Default;
        skyImporter.textureShape = TextureImporterShape.TextureCube;
        skyImporter.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
        skyImporter.sRGBTexture = false;
        skyImporter.maxTextureSize = 1024;
        skyImporter.mipmapEnabled = true;
        skyImporter.isReadable = false;
        skyImporter.textureCompression = TextureImporterCompression.CompressedHQ;
        TextureImporterSettings skySettings = new TextureImporterSettings();
        skyImporter.ReadTextureSettings(skySettings);
        skySettings.cubemapConvolution = TextureImporterCubemapConvolution.Specular;
        skyImporter.SetTextureSettings(skySettings);
        skyImporter.SaveAndReimport();
        Material sky = LoadOrCreateMaterial("CeuFotografico", "Skybox/Cubemap");
        sky.SetTexture("_Tex", RequireAsset<Cubemap>(SkyPath));
        sky.SetColor("_Tint", Color.gray);
        sky.SetFloat("_Exposure", 0.65f);
        sky.SetFloat("_Rotation", 160f);
        EditorUtility.SetDirty(sky);
        AssetDatabase.SaveAssets();
    }

    public static void Populate(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            throw new BuildFailedException("A cena precisa estar carregada para posicionar as rochas escaneadas.");
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == PropsName) UnityEngine.Object.DestroyImmediate(root);
        GameObject group = new GameObject(PropsName);
        SceneManager.MoveGameObjectToScene(group, scene);
        Material rockMaterial = RequireAsset<Material>(Materials + "/RochaEscaneada.mat");

        // The central corridor x = +/-4, z = -3..6 and all encounter posts remain clear.
        Vector4[] placements =
        {
            new Vector4(-8.8f, -0.02f, -8.3f, 0.80f), new Vector4(9.2f, -0.03f, -7.0f, 1.15f),
            new Vector4(-7.7f, -0.03f, -2.2f, 1.00f), new Vector4(8.4f, -0.02f, -1.0f, 1.05f),
            new Vector4(-7.2f, -0.02f, 5.6f, 0.90f), new Vector4(7.8f, -0.03f, 6.8f, 1.10f),
            new Vector4(-12.6f, -0.03f, 9.4f, 1.35f), new Vector4(12.8f, -0.03f, 9.0f, 1.30f),
            new Vector4(-9.3f, -0.02f, 13.7f, 0.90f), new Vector4(8.6f, -0.02f, 14.1f, 0.85f),
            new Vector4(-14.5f, -0.03f, -5.1f, 1.45f), new Vector4(14.5f, -0.03f, -5.6f, 1.25f),
            new Vector4(-3.5f, -0.02f, 14.0f, 0.65f), new Vector4(3.9f, -0.02f, 14.4f, 0.75f),
            new Vector4(-11.3f, -0.03f, -12.8f, 1.00f)
        };
        for (int index = 0; index < placements.Length; index++)
        {
            GameObject model = RequireAsset<GameObject>(RockPath(index % 6 + 1));
            GameObject prop = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            prop.name = "Rocha escaneada " + (index + 1).ToString("00");
            prop.transform.SetParent(group.transform, false);
            Vector4 placement = placements[index];
            prop.transform.SetPositionAndRotation(new Vector3(placement.x, placement.y, placement.z),
                Quaternion.Euler(0f, (index * 137f) % 360f, 0f));
            prop.transform.localScale = Vector3.one * placement.w;
            foreach (Transform child in prop.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = 0; // Obstacles must block movement and strikes, not count as enemies or ground.
                child.gameObject.isStatic = true;
            }
            foreach (Renderer renderer in prop.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++) materials[slot] = rockMaterial;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            foreach (MeshFilter mesh in prop.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshCollider collider = mesh.GetComponent<MeshCollider>();
                if (collider == null) collider = mesh.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh.sharedMesh;
            }
        }
        group.isStatic = true;
        Debug.Log("[AMBIENTE CC0] 15 rochas escaneadas posicionadas; corredor e pontos de combate livres.");
    }

    public static void ConfigureLighting()
    {
        RenderSettings.skybox = RequireAsset<Material>(Materials + "/CeuFotografico.mat");
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflection = RequireAsset<Cubemap>(SkyPath);
        RenderSettings.reflectionIntensity = 0.75f;
        RenderSettings.reflectionBounces = 1;
        // Preserve a deterministic diffuse fill without requiring a GI bake in this prototype.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.30f, 0.33f, 0.38f);
        RenderSettings.ambientEquatorColor = new Color(0.18f, 0.19f, 0.21f);
        RenderSettings.ambientGroundColor = new Color(0.09f, 0.08f, 0.07f);
        if (Camera.main != null) Camera.main.clearFlags = CameraClearFlags.Skybox;
    }

    private static void ConfigureTexture(string path, string channel)
    {
        TextureImporter importer = RequireImporter<TextureImporter>(path);
        importer.textureType = channel == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = channel == "Albedo";
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = true;
        importer.isReadable = false;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8;
        importer.mipMapBias = 0f;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static Material CreateSurface(string name, string surface, float repeat, float smoothness,
        float normalStrength, float occlusionStrength)
    {
        Material material = LoadOrCreateMaterial(name, "Standard");
        // The scan albedo already contains the rock/soil colour; a shared blue tint
        // was washing the different surfaces towards the same cold grey.
        material.color = Color.white;
        material.enableInstancing = true;
        foreach (string channel in new[] { "Albedo", "Normal", "AO", "MetallicSmoothness" })
        {
            string property = channel == "Albedo" ? "_MainTex" : channel == "Normal" ? "_BumpMap" :
                channel == "AO" ? "_OcclusionMap" : "_MetallicGlossMap";
            material.SetTexture(property, RequireAsset<Texture2D>(Root + "/" + surface + "/" + channel + ".png"));
            material.SetTextureScale(property, Vector2.one * repeat);
        }
        material.SetFloat("_BumpScale", normalStrength);
        material.SetFloat("_OcclusionStrength", occlusionStrength);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_GlossMapScale", smoothness);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.EnableKeyword("_NORMALMAP");
        material.EnableKeyword("_METALLICGLOSSMAP");
        material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        material.DisableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", Color.black);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material LoadOrCreateMaterial(string name, string shaderName)
    {
        string path = Materials + "/" + name + ".mat";
        Shader shader = Shader.Find(shaderName);
        if (shader == null) throw new BuildFailedException("Shader de ambiente ausente: " + shaderName);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else material.shader = shader;
        return material;
    }

    private static void Remap(ModelImporter importer, string sourceName, Material material)
    {
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), sourceName), material);
    }

    private static string RockPath(int index) => Root + "/Rocks/Rock" + index.ToString("00") + ".fbx";

    private static T RequireAsset<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) throw new BuildFailedException("Asset de ambiente ausente: " + path);
        return asset;
    }

    private static T RequireImporter<T>(string path) where T : AssetImporter
    {
        T importer = AssetImporter.GetAtPath(path) as T;
        if (importer == null) throw new BuildFailedException("Importador de ambiente ausente: " + path);
        return importer;
    }
}
