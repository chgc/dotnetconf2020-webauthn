using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using webauthn.entity.Entity;

namespace webauthn.entity
{
    public class WebAuthnDbConext: DbContext
    {
        public WebAuthnDbConext() { }
        public WebAuthnDbConext(DbContextOptions<WebAuthnDbConext> options) : base(options)
        {
        }

        public virtual DbSet<Member> Members { get; set; }
        public virtual DbSet<StoredCredential> StoredCredentials { get; set; }
    }
}
