using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RehearsalForecast.Core.Export;
using RehearsalForecast.Core.Forecast;
using RehearsalForecast.Core.Loan;
using RehearsalForecast.Core.Solving;
using RehearsalForecast.Core.Validation;
using RehearsalForecast.Web.ModelBinders;

// Composition root for the RehearsalForecast web application.
// Wires DI (design §11, §20.1), request-localization culture pinning
// (design §20.3, §20.5), console logging (design §20.1), the shared
// exception handler (design §14.3), the custom MonthlySchedule binder
// (design §11.4), MVC + antiforgery (design §11.5), and the default
// route (design §11.1). Requirements 20.4, 21.1, 21.5.

var builder = WebApplication.CreateBuilder(args);

// --- Logging (design §20.1) --------------------------------------------------
// Console-only sink; default Information, Microsoft.AspNetCore at Warning.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

// --- MVC + antiforgery + custom model binder (design §11.4, §11.5) ---------
// AddControllersWithViews enables antiforgery by default; the custom
// MonthlyScheduleModelBinderProvider must run before the default complex-type
// binder, so it is inserted at index 0.
builder.Services.AddControllersWithViews(options =>
{
    options.ModelBinderProviders.Insert(0, new MonthlyScheduleModelBinderProvider());
});

// --- Core service registrations (design §11, §20 DI policy) ----------------
// Each core service is a stateless calculation component; Scoped lifetime is
// appropriate for per-request use inside MVC actions.
builder.Services.AddScoped<ILoanCalculator, LoanCalculator>();
builder.Services.AddScoped<IForecastCalculator, ForecastCalculator>();
builder.Services.AddScoped<ISolver, PriceSolver>();
builder.Services.AddScoped<IInputValidator, InputValidator>();
builder.Services.AddScoped<ICsvExporter, CsvExporter>();

// --- Post-Redirect-Get result cache ---------------------------------------
// The Calculate action stores its ForecastResultViewModel under a GUID key
// and redirects to a GET /Forecast/Results/{id} action which reads back the
// entry. This makes the results page bookmark-safe and refresh-safe, and
// eliminates the pre-PRG "405 Method Not Allowed" the browser produced when
// the results URL was reloaded as a GET. Entries expire automatically
// (see ForecastController.ResultCacheTtl); on expiry the GET action
// redirects to the input page with a user-facing notice.
//
// NOTE: IMemoryCache is per-instance. This is safe today because the app
// runs with instance_count = 1; horizontal scale-out would require sticky
// sessions or a distributed cache (Redis, etc.) to keep the POST-side
// stash reachable from the GET-side lookup.
builder.Services.AddMemoryCache();

// --- Request-localization: pin en-US (design §20.3, §20.5) -----------------
var supportedCultures = new[] { CultureInfo.CreateSpecificCulture("en-US") };
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

var app = builder.Build();

// --- Exception handler (design §14.3) --------------------------------------
// Unhandled exceptions in any environment route to a generic /Error page.
// Business logic never throws, so this branch is reserved for environment or
// infrastructure failures. Detailed exception content is logged to stdout via
// the console logger (design §20.1); the response is a minimal error page.
app.UseExceptionHandler("/Error");

app.UseStaticFiles();

// Apply the configured localization options so the request culture is pinned
// before routing and controllers execute.
app.UseRequestLocalization();

app.UseRouting();

// Fallback /Error endpoint that returns a simple, static error page. Routed as
// a top-level endpoint so it works even if MVC routing fails to resolve a
// controller. Requirement 20.4: no PII, no exception details.
app.MapGet("/Error", () => Results.Content(
    """
    <!DOCTYPE html>
    <html lang="en">
    <head>
      <meta charset="utf-8" />
      <title>Rehearsal Forecast — Error</title>
    </head>
    <body>
      <h1>Something went wrong.</h1>
      <p>An unexpected error occurred while processing your request.</p>
      <p><a href="/">Return to the input page.</a></p>
    </body>
    </html>
    """,
    contentType: "text/html; charset=utf-8"));

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Forecast}/{action=Index}/{id?}");

app.Run();
