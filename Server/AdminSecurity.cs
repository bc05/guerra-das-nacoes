using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace GuerraDasNacoes.Server;

public sealed class AdminLoginRequest {public string secret;}
public sealed class AdminSecurity
{
    private readonly byte[] expected;
    private readonly object gate=new();
    private readonly Dictionary<string,double> sessions=new(StringComparer.Ordinal);
    private readonly Queue<double> failedAttempts=new();
    private const string Cookie="gdn_admin";
    public AdminSecurity(string databasePath)
    {
        string directory=Path.GetDirectoryName(Path.GetFullPath(databasePath));
        string path=Path.Combine(directory,"admin-secret.txt");
        if(!File.Exists(path))
        {
            var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
            if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            using var stream=new FileStream(path,options);
            byte[] value=Encoding.UTF8.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))+"\n");
            stream.Write(value);
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        }
        string secret=File.ReadAllText(path).Trim();
        if(secret.Length<40)throw new InvalidDataException("Chave administrativa local inválida.");
        expected=SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }
    public void ValidateOrigin(HttpContext context,bool requireHeader)
    {
        if(context.Connection.RemoteIpAddress==null||!IPAddress.IsLoopback(context.Connection.RemoteIpAddress))throw new GameApiException(403,"admin_local_only","Administração disponível somente neste computador.");
        // Reject DNS rebinding: the TCP origin alone is insufficient when a public host resolves to loopback.
        string host=context.Request.Host.Host;
        if(!(host.Equals("localhost",StringComparison.OrdinalIgnoreCase)||(IPAddress.TryParse(host,out var address)&&IPAddress.IsLoopback(address))))
            throw new GameApiException(403,"admin_host_invalid","Host administrativo inválido.");
        string origin=context.Request.Headers.Origin.ToString();
        if(!string.IsNullOrEmpty(origin) && (!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||uri.Scheme!=context.Request.Scheme||!uri.Authority.Equals(context.Request.Host.Value,StringComparison.OrdinalIgnoreCase)))
            throw new GameApiException(403,"admin_origin_invalid","Origem administrativa inválida.");
        if(requireHeader&&context.Request.Headers["X-Admin-Request"]!="1")throw new GameApiException(403,"admin_csrf_invalid","Cabeçalho administrativo obrigatório.");
        if(context.Request.Headers["Sec-Fetch-Site"]=="cross-site")throw new GameApiException(403,"admin_origin_invalid","Solicitação de outro site recusada.");
    }
    public object Login(HttpContext context,AdminLoginRequest request)
    {
        ValidateOrigin(context,true);
        double now=GameWorld.UnixNow;
        lock(gate)
        {
            while(failedAttempts.TryPeek(out double at)&&at<now-60)failedAttempts.Dequeue();
            if(failedAttempts.Count>=10)throw new GameApiException(429,"admin_rate_limited","Aguarde um minuto antes de tentar novamente.");
            byte[] supplied=SHA256.HashData(Encoding.UTF8.GetBytes(request?.secret??""));
            if(!CryptographicOperations.FixedTimeEquals(supplied,expected)){failedAttempts.Enqueue(now);throw new GameApiException(401,"admin_login_invalid","Chave administrativa inválida.");}
            string token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            sessions[token]=now+3600;
            context.Response.Cookies.Append(Cookie,token,new CookieOptions{HttpOnly=true,SameSite=SameSiteMode.Strict,Secure=context.Request.IsHttps,Path="/admin",MaxAge=TimeSpan.FromHours(1),IsEssential=true});
            return new {authenticated=true};
        }
    }
    public void Authenticate(HttpContext context,bool mutation)
    {
        ValidateOrigin(context,mutation);
        lock(gate)
        {
            double now=GameWorld.UnixNow;
            foreach(var token in sessions.Where(p=>p.Value<=now).Select(p=>p.Key).ToArray())sessions.Remove(token);
            if(!context.Request.Cookies.TryGetValue(Cookie,out string key)||!sessions.ContainsKey(key))throw new GameApiException(401,"admin_session_invalid","Entre no painel administrativo.");
        }
    }
    public object Logout(HttpContext context)
    {
        Authenticate(context,true);
        lock(gate){if(context.Request.Cookies.TryGetValue(Cookie,out string key))sessions.Remove(key);}
        context.Response.Cookies.Delete(Cookie,new CookieOptions{Path="/admin",HttpOnly=true,SameSite=SameSiteMode.Strict});
        return new {authenticated=false};
    }
}
