namespace DynLock.Addin.Auth
{
    internal static class AddinConfig
    {
        // Hardcoded intentionally: end users (employees/external customers) must not be able
        // to redirect the auth check to a server they control (no env var / config file override).
        // Only a new build can change this.
        public const string AuthServerUrl = "https://tools.bimlab.com.vn";
    }
}
