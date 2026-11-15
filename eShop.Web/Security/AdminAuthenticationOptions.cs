namespace eShop.Web.Security
{
    public sealed class AdminAuthenticationOptions
    {
        public string UserName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
    }
}
