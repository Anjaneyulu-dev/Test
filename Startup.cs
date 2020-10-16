using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ASPApple
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });


            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);

            services.AddAuthentication(options =>
           {
                options.DefaultAuthenticateScheme = "cookie";
                options.DefaultChallengeScheme = "apple";
            })
                .AddCookie("cookie")
                .AddOpenIdConnect("apple", async options =>
                {
                    options.ResponseType = "code";
                    options.SignInScheme = "cookie";
                    options.DisableTelemetry = true;
                    options.Scope.Clear(); // otherwise I had consent request issues

                    options.Configuration = new OpenIdConnectConfiguration
                    {
                        AuthorizationEndpoint = "https://appleid.apple.com/auth/authorize",
                        TokenEndpoint = "https://appleid.apple.com/auth/token",
                    };

                    //options.ClientId = "com.scottbrady91.authdemo.service"; // Service ID
                    options.ClientId = "com.apoyar.sai.service"; // Service ID                    
                    options.CallbackPath = "/signin-apple"; // corresponding to our redirect URI

                    options.Events.OnAuthorizationCodeReceived = context =>
                    {
                        context.TokenEndpointRequest.ClientSecret = TokenGenerator.CreateNewToken();
                        return Task.CompletedTask;
                    };

                    // Expected identity token iss value
                    options.TokenValidationParameters.ValidIssuer = "https://appleid.apple.com";

                    // Expected identity token signing key
                    var jwks = await new HttpClient().GetStringAsync("https://appleid.apple.com/auth/keys");
                    options.TokenValidationParameters.IssuerSigningKey = new JsonWebKeySet(jwks).Keys.FirstOrDefault();

                    // Disable nonce validation (not supported by Apple)
                    options.ProtocolValidator.RequireNonce = false;
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
    public static class TokenGenerator
    {
        public static string CreateNewToken()
        {
            const string iss = "62QM29578N"; // your accounts team ID found in the dev portal
            const string aud = "https://appleid.apple.com";
            const string sub = "com.scottbrady91.authdemo.service"; // same as client_id

            const string privateKey = "MIGTAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBHkwdwIBAQQgnbfHJQO9feC7yKOenScNctvHUP+Hp3AdOKnjUC3Ee9GgCgYIKoZIzj0DAQehRANCAATMgckuqQ1MhKALhLT/CA9lZrLA+VqTW/iIJ9GKimtC2GP02hCc5Vac8WuN6YjynF3JPWKTYjg2zqex5Sdn9Wj+";
            var cngKey = CngKey.Import(Convert.FromBase64String(privateKey), CngKeyBlobFormat.Pkcs8PrivateBlob);

            var handler = new JwtSecurityTokenHandler();
            var token = handler.CreateJwtSecurityToken(
                issuer: iss,
                audience: aud,
                subject: new ClaimsIdentity(new List<Claim> { new Claim("sub", sub) }),
                expires: DateTime.UtcNow.AddMinutes(5), // expiry can be a maximum of 6 months => generate one per request, or one and then re-use until expiration
                issuedAt: DateTime.UtcNow,
                notBefore: DateTime.UtcNow,
                signingCredentials: new SigningCredentials(new ECDsaSecurityKey(new ECDsaCng(cngKey)), SecurityAlgorithms.EcdsaSha256));

            return handler.WriteToken(token);
        }
    }
}
