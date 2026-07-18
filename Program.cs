using IdentityBridge.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// --- Strategy: one PolicyScheme dispatches each request to the right cookie scheme.
// It only selects (dispatches) — it never constructs an adapter. Selection here is by
// ?idp= query for the MVP simulate flow; a real fork keys off path/subdomain/session.
const string EntraCookie = "EntraCookie";
const string Auth0Cookie = "Auth0Cookie";
const string CognitoCookie = "CognitoCookie";

// Invariant: each provider gets its OWN dedicated cookie name — never shared.
builder.Services.AddAuthentication("IdentityBridge")
    // The PolicyScheme is the "front door" for all requests. It dispatches to the right cookie scheme.
    .AddPolicyScheme("IdentityBridge", "IdentityBridge", options =>
    {
        // The selector runs on every request. It must be fast and side-effect-free. It only selects the scheme to use for this request; it never constructs an adapter or reads the cookie.
        options.ForwardDefaultSelector = context =>
        {
            var idp = context.Request.Query["idp"].ToString();
            return idp switch
            {
                "auth0" => Auth0Cookie,
                "cognito" => CognitoCookie,
                _ => EntraCookie
            };
        };
    })
    // Invariant: each provider gets its OWN dedicated cookie name — never shared.
    .AddCookie(EntraCookie, o => o.Cookie.Name = "ib.entra")
    .AddCookie(Auth0Cookie, o => o.Cookie.Name = "ib.auth0")
    .AddCookie(CognitoCookie, o => o.Cookie.Name = "ib.cognito");

builder.Services.AddAuthorization();

// Adapter + Factory wiring. Callers depend only on the abstractions.
builder.Services.AddTransient<IClaimsTransformation, IdentityBridgeClaimsTransformation>(); // IClaimsTransformation MS default guidance : always register as transient, never singleton.(run on every request). Stateless Singletons are safe here because they have no state, but transient is the recommended lifetime for IClaimsTransformation.
builder.Services.AddSingleton<IUserProfileService, EntraUserProfileService>();
builder.Services.AddSingleton<IUserProfileService, Auth0UserProfileService>();
builder.Services.AddSingleton<IUserProfileService, CognitoUserProfileService>();
builder.Services.AddSingleton<IUserProfileServiceFactory, UserProfileServiceFactory>();

// Multi-node note: cookies are Data-Protection-encrypted. Running on >1 node requires a
// shared key ring, else each node rejects the others' cookies. Wire per project, e.g.:
// builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(@"\\share\keys"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
