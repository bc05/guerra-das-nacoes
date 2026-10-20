using UnityEditor;
using UnityEngine;

public static class FrontierReleaseSetup
{
    [MenuItem("Guerra das Nações/Aplicar fronteira e conferir entrega")]
    public static void ApplyAndCapture()
    {
        VisualRefinementSetup.Apply();
        WorldExpansionSetup.Apply();
        ServerWorldExporter.Export();
        MonsterDangerChecks.Run();
        WorldExpansionPreview.Capture();
        PrototypeSetup.RenderPreview();
        Debug.Log("[FRONTIER RELEASE PASS] Arte, vale, colisões, perigo por nível e capturas concluídos na cena salva.");
    }
}
