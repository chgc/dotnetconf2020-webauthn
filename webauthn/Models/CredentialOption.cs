using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace webauthn.Models
{
    public class CredentialOption
    {
        public string UserName { get; set; }
        public string DisplayName { get; set; }
        // possible values: none, direct, indirect
        public string AttType { get; set; } = "none";
        // possible values: <empty>, platform, cross-platform
        public string AuthType { get; set; }
        // possible values: preferred, required, discouraged
        public string UserVerification { get; set; } = "discouraged";

        public bool RequireResidentKey { get; set; }
    }
}
