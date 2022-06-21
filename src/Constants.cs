namespace CallCentre
{
    public static class Constants
    {
        public const string UpdateUrl = "https://raw.githubusercontent.com/femma-it/call-centre/master/version.xml";
#if DEBUG
        public const string ApiUrl = "https://localhost:5011";
#else
        public const string ApiUrl = "https://femma.qexal.app";
#endif
        public const string HubUrl = ApiUrl + "/api/hubs/microsip";
        public const string AutoProvisioningUrl = ApiUrl + "/api/v1/microsip/configuration";
        public const string ClientId = "microsip";
        public const string Scope = "openid profile offline_access Qexal.ApiAPI";
    }
}