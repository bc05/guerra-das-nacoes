using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class PrototypeBranding
{
    public const string IconPath = "Assets/Art/Brand/GameIcon.png";

    // O nome visível pode ter acentos; o executável ASCII evita diferenças
    // de normalização Unicode entre o sistema de arquivos e a assinatura.
    public static void ConfigureMacOSBundle(string appPath)
    {
        string plistPath = Path.Combine(appPath, "Contents/Info.plist");
        XDocument plist = XDocument.Load(plistPath);
        XElement dictionary = plist.Root.Element("dict");
        string executable = dictionary.Elements("key")
            .Single(key => key.Value == "CFBundleExecutable")
            .ElementsAfterSelf().First().Value;
        if (string.IsNullOrEmpty(executable) || Path.GetFileName(executable) != executable)
            throw new BuildFailedException("Nome de executável macOS inválido.");

        const string executableName = "GuerraDasNacoes";
        string executableDirectory = Path.Combine(appPath, "Contents/MacOS");
        string source = Path.Combine(executableDirectory, executable);
        string destination = Path.Combine(executableDirectory, executableName);
        if (!File.Exists(source))
            throw new BuildFailedException("Executável macOS não encontrado: " + source);
        if (executable != executableName)
        {
            // Um rebuild pode conservar o executável ASCII da versão anterior.
            if (File.Exists(destination)) File.Delete(destination);
            File.Move(source, destination);
        }
        SetPlistString(dictionary, "CFBundleExecutable", executableName);
        SetPlistString(dictionary, "CFBundleName", "Guerra das Nações");
        SetPlistString(dictionary, "CFBundleDisplayName", "Guerra das Nações");
        // Mono serializa o DTD carregado com um subconjunto vazio ("[]"), recusado
        // pelos parsers do macOS. O plist XML não exige a declaração de DTD.
        plist.DocumentType?.Remove();
        plist.Save(plistPath);
        RunMacOSTool("/usr/bin/plutil", "-lint", plistPath, "Info.plist incompatível com o macOS");

        // Alterar o plist exige renovar a assinatura local do aplicativo.
        RunMacOSTool("/usr/bin/codesign", "--force --deep --sign - --preserve-metadata=entitlements,flags,runtime", appPath, "Falha na assinatura local macOS");
        RunMacOSTool("/usr/bin/codesign", "--verify --deep --strict", appPath, "Falha na verificação da assinatura local macOS");
        Debug.Log("[BRANDING PASS] Nome macOS corrigido, Info.plist aceito pelo macOS e assinatura local verificada.");
    }

    private static void SetPlistString(XElement dictionary, string name, string value)
    {
        XElement key = dictionary.Elements("key").SingleOrDefault(element => element.Value == name);
        if (key == null) dictionary.Add(new XElement("key", name), new XElement("string", value));
        else key.ElementsAfterSelf().First().Value = value;
    }

    private static void RunMacOSTool(string tool, string arguments, string path, string failure)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo(tool,
            arguments + " \"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using (var process = System.Diagnostics.Process.Start(startInfo))
        {
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new BuildFailedException(failure + ": " + error);
        }
    }

    [MenuItem("Guerra das Nações/Configurar ícone do jogo")]
    public static void Configure()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Ícone do jogo ausente: " + IconPath);
        importer.textureType = TextureImporterType.Default;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (icon == null || icon.width != 1024 || icon.height != 1024)
            throw new InvalidOperationException("O ícone deve ter 1024 × 1024 pixels.");
        int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
        if (sizes.Length == 0) throw new InvalidOperationException("Nenhum tamanho de ícone disponível para desktop.");
        PlayerSettings.SetIcons(NamedBuildTarget.Standalone, sizes.Select(_ => icon).ToArray(), IconKind.Any);
        AssetDatabase.SaveAssets();
        Debug.Log("[BRANDING PASS] Ícone original configurado nos " + sizes.Length + " tamanhos de desktop.");
    }
}
