using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using webauthn.Models;
using webauthn.entity;
using webauthn.entity.Entity;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Fido2NetLib.Objects;
using Fido2NetLib;
using static Fido2NetLib.Fido2;

namespace webauthn.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IFido2 _fido2;
        private readonly WebAuthnDbConext context;

        public AuthController(IFido2 fido2, WebAuthnDbConext context)
        {
            _fido2 = fido2;
            this.context = context;
        }

        /// <summary>
        /// 註冊使用者並回傳 Credential Option
        /// </summary>
        /// <returns></returns>
        [HttpPost("makeCredentialOptions")]
        public async Task<ActionResult<CredentialCreateOptions>> MakeCredentialOptions(CredentialOption option)
        {
            try
            {
                // 註冊使用者
                var user = await context.Members.Where(x => x.UserName == option.UserName).FirstOrDefaultAsync();
                if (user == null)
                {

                    user = new Member
                    {
                        MemberId = Guid.NewGuid(),
                        UserName = option.UserName,
                        DisplayName = option.DisplayName,
                        UserId = Encoding.UTF8.GetBytes(option.UserName)
                    };
                    context.Members.Add(user);
                    await context.SaveChangesAsync();
                }
                var fidoUser = new Fido2User
                {
                    DisplayName = user.DisplayName,
                    Name = user.UserName,
                    Id = user.UserId
                };
                // 取得 Key. 排除已經註冊過的 Credentials              
                var existingKeys = await context.StoredCredentials.Where(x => x.UserId == user.UserId).Select(x => x.Descriptor).ToListAsync();

                // 建立 Option
                var authenticatorSelection = new AuthenticatorSelection
                {
                    RequireResidentKey = option.RequireResidentKey,
                    UserVerification = option.UserVerification.ToEnum<UserVerificationRequirement>()
                };
                if (!string.IsNullOrEmpty(option.AuthType))
                {
                    authenticatorSelection.AuthenticatorAttachment = option.AuthType.ToEnum<AuthenticatorAttachment>();
                }

                var exts = new AuthenticationExtensionsClientInputs() { Extensions = true, UserVerificationIndex = true, Location = true, UserVerificationMethod = true, BiometricAuthenticatorPerformanceBounds = new AuthenticatorBiometricPerfBounds { FAR = float.MaxValue, FRR = float.MaxValue } };

                var options = _fido2.RequestNewCredential(fidoUser, existingKeys, authenticatorSelection, option.AttType.ToEnum<AttestationConveyancePreference>(), exts);

                // Temporarily store options, session/in-memory cache/redis/db
                HttpContext.Session.SetString("fido2.attestationOptions", options.ToJson());

                // 回傳
                return Ok(options);
            }
            catch (Exception e)
            {
                return BadRequest(new CredentialCreateOptions { Status = "error", ErrorMessage = FormatException(e) });
            }
        }


        [HttpPost("makeCredential")]
        public async Task<ActionResult<CredentialMakeResult>> MakeCredential(AuthenticatorAttestationRawResponse attestationResponse)
        {
            try
            {
                // 1. get the options we sent the client
                var jsonOptions = HttpContext.Session.GetString("fido2.attestationOptions");
                var options = CredentialCreateOptions.FromJson(jsonOptions);

                // 2. Create callback so that lib can verify credential id is unique to this user
                IsCredentialIdUniqueToUserAsyncDelegate callback = async (IsCredentialIdUniqueToUserParams args) =>
                {
                    var credentialIdString = Base64Url.Encode(args.CredentialId);
                    var cred = await context.StoredCredentials.Where(x => x.DescriptorJson.Contains(credentialIdString)).FirstOrDefaultAsync();
                    if (cred == null)
                    {
                        return true;
                    }
                    var users = await context.Members.Where(x => x.UserId == cred.UserId).Select(u => new Fido2User
                    {
                        DisplayName = u.DisplayName,
                        Name = u.UserName,
                        Id = u.UserId
                    }).ToListAsync();
                    return users.Count == 0;
                };

                // 2. Verify and make the credentials
                var success = await _fido2.MakeNewCredentialAsync(attestationResponse, options, callback);

                context.StoredCredentials.Add(new StoredCredential
                {
                    Descriptor = new PublicKeyCredentialDescriptor(success.Result.CredentialId),
                    UserId = options.User.Id,
                    PublicKey = success.Result.PublicKey,
                    UserHandle = success.Result.User.Id,
                    SignatureCounter = success.Result.Counter,
                    CredType = success.Result.CredType,
                    RegDate = DateTime.Now,
                    AaGuid = success.Result.Aaguid
                });
                await context.SaveChangesAsync();

                return Ok(success);
            }
            catch (Exception e)
            {
                return BadRequest(new CredentialMakeResult { Status = "error", ErrorMessage = FormatException(e) });
            }
        }


        [HttpPost("assertionOptions")]
        public async Task<IActionResult> AssertionOptionsPost(AssertionOptionsModel model)
        {
            try
            {
                var existingCredentials = new List<PublicKeyCredentialDescriptor>();

                if (!string.IsNullOrEmpty(model.UserName))
                {
                    // 1. Get user from DB
                    var user = await context.Members.Where(x => x.UserName == model.UserName).FirstOrDefaultAsync();
                    if (user == null)
                        return BadRequest("Username was not registered");

                    // 2. Get registered credentials from database
                    existingCredentials = await context.StoredCredentials.Where(x => x.UserId == user.UserId).Select(m => m.Descriptor).ToListAsync();
                }

                var exts = new AuthenticationExtensionsClientInputs()
                {
                    SimpleTransactionAuthorization = "Could you please verify yourself?",
                    GenericTransactionAuthorization = new TxAuthGenericArg
                    {
                        ContentType = "text/plain",
                        Content = new byte[] { 0x46, 0x49, 0x44, 0x4F }
                    },
                    UserVerificationIndex = true,
                    Location = true,
                    UserVerificationMethod = true
                };

                // 3. Create options
                var uv = string.IsNullOrEmpty(model.UserVerification) ? UserVerificationRequirement.Discouraged : model.UserVerification.ToEnum<UserVerificationRequirement>();
                var options = _fido2.GetAssertionOptions(
                    existingCredentials,
                    uv,
                    exts
                );

                // 4. Temporarily store options, session/in-memory cache/redis/db
                HttpContext.Session.SetString("fido2.assertionOptions", options.ToJson());

                // 5. Return options to client
                return Ok(options);
            }

            catch (Exception e)
            {
                return BadRequest(new AssertionOptions { Status = "error", ErrorMessage = FormatException(e) });
            }
        }

        [HttpPost("makeAssertion")]
        public async Task<IActionResult> MakeAssertion(AuthenticatorAssertionRawResponse clientResponse)
        {
            try
            {
                // 1. Get the assertion options we sent the client
                var jsonOptions = HttpContext.Session.GetString("fido2.assertionOptions");
                var options = AssertionOptions.FromJson(jsonOptions);

                // 2. Get registered credential from database                
                var credentialIdString = Base64Url.Encode(clientResponse.Id);
                var cred = await context.StoredCredentials.Where(x => x.DescriptorJson.Contains(credentialIdString)).FirstOrDefaultAsync();
                if (cred == null)
                {
                    throw new Exception("Unknown credentials");
                }

                // 3. Get credential counter from database
                var storedCounter = cred.SignatureCounter;

                // 4. Create callback to check if userhandle owns the credentialId
                IsUserHandleOwnerOfCredentialIdAsync callback = async (args) =>
                {
                    var storedCreds = await context.StoredCredentials.Where(x => x.UserHandle == args.UserHandle).ToListAsync();
                    return storedCreds.Exists(c => c.Descriptor.Id.SequenceEqual(args.CredentialId));
                };

                // 5. Make the assertion
                var res = await _fido2.MakeAssertionAsync(clientResponse, options, cred.PublicKey, storedCounter, callback);

                // 6. Store the updated counter            
                cred.SignatureCounter = res.Counter;
                await context.SaveChangesAsync();

                // 7. return OK to client
                return Ok(res);
            }
            catch (Exception e)
            {
                return BadRequest(new AssertionVerificationResult { Status = "error", ErrorMessage = FormatException(e) });
            }
        }

        private string FormatException(Exception e)
        {
            return string.Format("{0}{1}", e.Message, e.InnerException != null ? " (" + e.InnerException.Message + ")" : "");
        }
    }
}
