using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Explicit imports keep the source UV maps and avoid Blender's legacy shaders.
public static class ThirdPartyCharacterMaterials
{
    public static void Configure()
    {
        foreach (string folder in new[] { "Knight", "Troll" })
        foreach (string path in Directory.GetFiles("Assets/Art/ThirdParty/" + folder, "*.png"))
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new BuildFailedException("Textura externa não importada: " + path);
            string name = Path.GetFileNameWithoutExtension(path);
            bool normal = name.EndsWith("Normal", StringComparison.Ordinal);
            bool albedo = name.EndsWith("Albedo", StringComparison.Ordinal);
            bool cutout = name == "TrollCloth_Albedo";
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = albedo;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            // The atlas backgrounds contain transparent white/black pixels. Dilating the
            // visible albedo prevents pale seams when the camera samples smaller mip levels.
            // Do not apply this colour operation to normal or metallic/smoothness data.
            importer.alphaIsTransparency = albedo;
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = cutout;
            importer.alphaTestReferenceValue = .5f;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.mipMapBias = 0f;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        // Preserve the author's per-pixel metal/roughness variation instead of flattening
        // every armour panel. Softer micro normals avoid sparkling at the gameplay distance.
        Material knight = Surface("MAT_Knight", "Knight/Knight", Color.white, .46f, .65f, .65f, true);
        Material skin = Surface("MAT_TrollSkin", "Troll/TrollSkin", new Color(.76f, .81f, .71f), .18f, .9f, .8f, false);
        Material cloth = Surface("MAT_TrollCloth", "Troll/TrollCloth", new Color(.9f, .86f, .8f), .10f, .55f, .85f, false);
        // The imported loincloth uses alpha for torn edges. Opaque mode fills the holes white.
        cloth.SetOverrideTag("RenderType", "TransparentCutout");
        cloth.SetFloat("_Mode", 1f);
        cloth.SetFloat("_Cutoff", .5f);
        cloth.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        cloth.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        cloth.SetInt("_ZWrite", 1);
        cloth.EnableKeyword("_ALPHATEST_ON");
        cloth.DisableKeyword("_ALPHABLEND_ON");
        cloth.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        cloth.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        Material eyes = Surface("MAT_TrollEyes", null, Color.white, .62f, 0f, 0f, false);
        SetMap(eyes, "_MainTex", "Assets/Art/ThirdParty/Troll/TrollEyes_Albedo.png", null, true);
        Material elite = Surface("MAT_TrollSkinElite", "Troll/TrollSkin", new Color(.59f, .66f, .61f), .18f, .9f, .8f, false);
        Remap("Assets/Art/Cavaleiro.fbx", knight);
        Remap("Assets/Art/Troll.fbx", skin, cloth, eyes);
        EditorUtility.SetDirty(elite);
        AssetDatabase.SaveAssets();
    }

    public static void ApplyElite(GameObject visual)
    {
        Material elite = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MAT_TrollSkinElite.mat");
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] != null && materials[i].name == "MAT_TrollSkin") materials[i] = elite;
            renderer.sharedMaterials = materials;
        }
    }

    private static Material Surface(string name, string prefix, Color tint, float smoothness,
        float normalStrength, float occlusionStrength, bool metallic)
    {
        string path = "Assets/Materials/" + name + ".mat";
        Shader shader = Shader.Find("Standard");
        if (shader == null) throw new BuildFailedException("Shader Standard ausente: " + name);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else material.shader = shader;
        // Reset opaque state first so rerunning the configurator also clears old variants.
        material.SetOverrideTag("RenderType", "");
        material.SetFloat("_Mode", 0f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        material.SetInt("_ZWrite", 1);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        material.DisableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", Color.black);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        material.renderQueue = -1;
        material.enableInstancing = true;
        material.color = tint;
        material.SetFloat("_Metallic", metallic ? 1f : 0f);
        material.SetFloat("_Glossiness", smoothness);
        material.SetFloat("_GlossMapScale", 1f);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.SetFloat("_BumpScale", normalStrength);
        material.SetFloat("_OcclusionStrength", occlusionStrength);
        material.SetTextureScale("_MainTex", Vector2.one);
        material.SetTextureScale("_BumpMap", Vector2.one);
        if (prefix != null)
        {
            string basePath = "Assets/Art/ThirdParty/" + prefix;
            SetMap(material, "_MainTex", basePath + "_Albedo.png", null, true);
            SetMap(material, "_BumpMap", basePath + "_Normal.png", "_NORMALMAP", true);
            SetMap(material, "_OcclusionMap", basePath + "_AO.png", null, false);
            SetMap(material, "_MetallicGlossMap", basePath + "_MetallicSmoothness.png", "_METALLICGLOSSMAP", metallic);
        }
        else
        {
            material.SetTexture("_MainTex", null);
            material.SetTexture("_BumpMap", null);
            material.SetTexture("_OcclusionMap", null);
            material.SetTexture("_MetallicGlossMap", null);
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_METALLICGLOSSMAP");
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void SetMap(Material material, string property, string path, string keyword, bool required)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (required && texture == null) throw new BuildFailedException("Mapa ausente: " + path);
        material.SetTexture(property, texture);
        if (keyword != null)
        {
            if (texture != null) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }
    }

    private static void Remap(string path, params Material[] materials)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) throw new BuildFailedException("Modelo externo ausente: " + path);
        foreach (Material material in materials)
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
        importer.SaveAndReimport();
    }
}
