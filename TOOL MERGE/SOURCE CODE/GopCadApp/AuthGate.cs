using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace GopCadApp;

internal static class AuthGate
{
    // Hardcoded intentionally: end users must not be able to redirect the
    // auth check to a server they control (no env var / config file
    // override). Only a new build can change this.
    private const string AuthServerUrl = "https://tools.bimlab.com.vn";

    // One shared, reused HttpClient: keeps the TCP/TLS connection to the
    // auth server's host alive between Prewarm() and the real login check
    // (a new HttpClient per call, as before, throws that connection away
    // each time). Also the generally-recommended way to use HttpClient.
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    // Fire-and-forget as soon as the app starts, before the user has even
    // seen the login form. By the time they type an email and click
    // "Đăng nhập", the TLS handshake to tools.bimlab.com.vn is already
    // done, so the real check only pays for the request itself. Hits
    // /api/health (not rate-limited, not proxied to the backend - nginx
    // answers 404 directly) purely to warm the connection.
    public static void Prewarm()
    {
        _ = Task.Run(async () =>
        {
            try { await Client.GetAsync($"{AuthServerUrl}/api/health"); }
            catch { /* best-effort only */ }
        });
    }

    public static async Task<(bool Ok, string FullName, string Error)> TryLoginAsync(string rawEmail)
    {
        string email = (rawEmail ?? "").Trim().ToLowerInvariant();
        if (email.Length == 0 || !email.Contains('@') || !email.Contains('.'))
        {
            return (false, "", "Gmail không hợp lệ.");
        }

        try
        {
            using HttpResponseMessage resp = await Client.GetAsync(
                $"{AuthServerUrl}/api/auth/check?email={Uri.EscapeDataString(email)}");

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return (false, "", "Gmail này chưa được cấp quyền truy cập.");
            }

            if (!resp.IsSuccessStatusCode)
            {
                return (false, "", $"Máy chủ xác thực trả lỗi ({(int)resp.StatusCode}).");
            }

            string json = await resp.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            bool isActive = root.TryGetProperty("isActive", out JsonElement activeEl) && activeEl.GetBoolean();
            if (!isActive)
            {
                return (false, "", "Gmail này chưa được cấp quyền truy cập.");
            }

            string fullName = root.TryGetProperty("fullName", out JsonElement nameEl) ? nameEl.GetString() ?? "" : "";
            return (true, fullName, "");
        }
        catch (Exception ex)
        {
            return (false, "", "Không kết nối được máy chủ xác thực: " + ex.Message);
        }
    }
}
