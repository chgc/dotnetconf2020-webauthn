using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace webauthn.Models
{
    public class AssertionOptionsModel
    {
        public string UserName { get; set; }
        public string UserVerification { get; set; }
    }
}
