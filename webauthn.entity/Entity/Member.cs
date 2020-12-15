using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace webauthn.entity.Entity
{
    public class Member
    {
        [Key]
        public Guid MemberId { get; set; }
        public string UserName { get; set; }
        public string DisplayName { get; set; }
        public byte[] UserId { get; set; }
    }
}
