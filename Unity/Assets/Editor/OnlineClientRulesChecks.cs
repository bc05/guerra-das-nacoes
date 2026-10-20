using System;
using System.IO;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class OnlineClientRulesChecks
{
    [MenuItem("Guerra das Nações/Validar identidade e configuração online")]
    public static void Run()
    {
        CheckServerAddresses();
        CheckProfiles();
        CheckIdentity();
        Debug.Log("[ONLINE CLIENT RULES PASS] Identidade aleatória estável, escrita atômica, corrupção preservada, perfis isolados e HTTPS validados.");
    }

    private static void CheckServerAddresses()
    {
        Equal("http://127.0.0.1:8088", PrototypeOnlineClient.ValidateServer("http://127.0.0.1:8088/", false), "loopback padrão");
        Equal("http://localhost:8088", PrototypeOnlineClient.ValidateServer("http://localhost:8088", false), "localhost");
        Equal("http://[::1]:8088", PrototypeOnlineClient.ValidateServer("http://[::1]:8088", false), "loopback IPv6");
        Equal("https://jogo.example:9443", PrototypeOnlineClient.ValidateServer("https://jogo.example:9443", false), "HTTPS remoto");
        Reject(() => PrototypeOnlineClient.ValidateServer("http://192.168.1.10:8088", false), "HTTP LAN precisa ser explícito");
        Equal("http://192.168.1.10:8088", PrototypeOnlineClient.ValidateServer("http://192.168.1.10:8088", true), "HTTP LAN autorizado por flag");
        Reject(() => PrototypeOnlineClient.ValidateServer("file:///tmp/game", true), "protocolo não HTTP");
        Reject(() => PrototypeOnlineClient.ValidateServer("https://user:secret@jogo.example", false), "credencial no endereço");
        Reject(() => PrototypeOnlineClient.ValidateServer("https://jogo.example/?token=secret", false), "query no endereço");
        Reject(() => PrototypeOnlineClient.ValidateServer("https://jogo.example/#fragment", false), "fragmento no endereço");
        Reject(() => PrototypeOnlineClient.ValidateServer("https://jogo.example/api", false), "caminho não suportado");
    }

    private static void CheckProfiles()
    {
        OnlineIdentityStore.ValidateProfile("principal");
        OnlineIdentityStore.ValidateProfile("jogador_2-teste");
        Reject(() => OnlineIdentityStore.ValidateProfile("../outro"), "travessia por perfil");
        Reject(() => OnlineIdentityStore.ValidateProfile("/absoluto"), "perfil absoluto");
        Reject(() => OnlineIdentityStore.ValidateProfile(""), "perfil vazio");
        Reject(() => OnlineIdentityStore.ValidateProfile(new string('a', 33)), "perfil longo");
    }

    private static void CheckIdentity()
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "GuerraOnlineIdentityCheck-" + Guid.NewGuid().ToString("N"));
        try
        {
            OnlineIdentity first = OnlineIdentityStore.LoadOrCreate(temporaryRoot, "principal");
            Require(Convert.FromBase64String(first.deviceKey).Length == 32, "segredo contém 256 bits");
            string firstPath = OnlineIdentityStore.GetIdentityPath(temporaryRoot, "principal");
            string originalFile = File.ReadAllText(firstPath);
            OnlineIdentity second = OnlineIdentityStore.LoadOrCreate(temporaryRoot, "principal");
            Equal(first.deviceKey, second.deviceKey, "mesmo perfil preserva identidade");
            Equal(originalFile, File.ReadAllText(firstPath), "reabertura não regrava credencial");
            OnlineIdentity other = OnlineIdentityStore.LoadOrCreate(temporaryRoot, "segundo");
            Require(other.deviceKey != first.deviceKey, "perfis independentes usam identidades diferentes");
            Require(Directory.GetFiles(Path.GetDirectoryName(firstPath), "*.tmp").Length == 0, "rename atômico remove temporário");
            File.WriteAllText(firstPath, "{\"version\":1,\"deviceKey\":\"damaged\"}");
            string corrupted = File.ReadAllText(firstPath);
            RejectDamaged(() => OnlineIdentityStore.LoadOrCreate(temporaryRoot, "principal"), "credencial inválida não recria personagem");
            Equal(corrupted, File.ReadAllText(firstPath), "arquivo corrompido é preservado");
            File.WriteAllText(firstPath, "{\"version\":2,\"deviceKey\":\"" + first.deviceKey + "\"}");
            RejectDamaged(() => OnlineIdentityStore.LoadOrCreate(temporaryRoot, "principal"), "versão futura não é sobrescrita");
            File.WriteAllText(firstPath, new string('x', 2049));
            RejectDamaged(() => OnlineIdentityStore.LoadOrCreate(temporaryRoot, "principal"), "arquivo desproporcional é recusado");
        }
        finally { if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true); }
    }

    private static void Equal(string expected, string actual, string context) => Require(expected == actual, context);
    private static void Require(bool condition, string context)
    {
        if (!condition) throw new BuildFailedException("Online: " + context);
    }
    private static void Reject(Action action, string context)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new BuildFailedException("Online deveria recusar: " + context);
    }
    private static void RejectDamaged(Action action, string context)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new BuildFailedException("Online deveria preservar erro: " + context);
    }
}
