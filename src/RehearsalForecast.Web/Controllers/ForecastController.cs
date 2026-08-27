using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using RehearsalForecast.Core.Export;
using RehearsalForecast.Core.Solving;
using RehearsalForecast.Core.Validation;
using RehearsalForecast.Web.ViewModels;

namespace RehearsalForecast.Web.Controllers;

/// <summary>
/// The application's only controller. Owns the input page, the calculation
/// action that produces the results page, and the CSV export action
/// (design §11.1, Requirements 2.11–2.13, 15.13, 17.2–17.5, 18.7–18.8,
/// 27.7, 27.9).
/// </summary>
/// <remarks>
/// <para>
/// Both POST actions guard the calculator/solver behind the same
/// validate-then-solve pipeline: model-binding annotations are checked via
/// <see cref="ControllerBase.ModelState"/>, then cross-field and structural
/// rules are checked via <see cref="IInputValidator"/> (design §10.3).
/// Neither the calculator nor the solver runs when validation fails
/// (Requirements 2.13, 27.9).
/// </para>
/// <para>
/// The results page follows the Post-Redirect-Get pattern: a successful
/// <see cref="Calculate(ForecastInputViewModel)"/> POST caches the produced
/// <see cref="ForecastResultViewModel"/> under a fresh <see cref="Guid"/>
/// key in <see cref="IMemoryCache"/> and 302-redirects to
/// <see cref="Results"/>, which renders the results view. This makes the
/// results URL bookmark-safe and refresh-safe (revised design §11.6). The
/// cached entry is subject to <see cref="ResultCacheTtl"/>; on expiry the
/// GET action redirects to <see cref="Index"/> with a TempData-carried
/// notice. The old inline hidden-form round-trip on the results page is
/// retained only so <see cref="ExportCsv"/> can rebind the exact same view
/// model deterministically without a second cache lookup.
/// </para>
/// <para>
/// When the solver breaches its safety limit (Requirement 15.11), the
/// same PRG flow applies: <see cref="Calculate(ForecastInputViewModel)"/>
/// caches a <see cref="ForecastResultViewModel"/> whose
/// <see cref="ForecastResultViewModel.SolverFailureMessage"/> is populated
/// and whose <see cref="ForecastResultViewModel.Result"/> is
/// <see langword="null"/> (Requirement 27.7), then redirects to
/// <see cref="Results"/>; <see cref="ExportCsv"/> redirects back to
/// <see cref="Index"/> with a TempData-carried error message and refuses to
/// emit CSV (design §14.2, Requirement 18.8).
/// </para>
/// </remarks>
public sealed class ForecastController : Controller
{
    private readonly IInputValidator _validator;
    private readonly ISolver _solver;
    private readonly ICsvExporter _csvExporter;
    private readonly IMemoryCache _resultCache;

    /// <summary>TempData key used by <see cref="ExportCsv"/> to surface a solver-failure banner on the redirected input page.</summary>
    internal const string ExportErrorTempDataKey = "ExportError";

    /// <summary>TempData key used by <see cref="Results"/> to surface a "results expired" notice on the redirected input page.</summary>
    internal const string ResultsExpiredTempDataKey = "ResultsExpired";

    /// <summary>Prefix applied to the <see cref="IMemoryCache"/> key so cache entries produced by this controller are namespaced.</summary>
    private const string ResultCacheKeyPrefix = "forecast:result:";

    /// <summary>How long a cached <see cref="ForecastResultViewModel"/> remains addressable via <see cref="Results"/>. Chosen to comfortably outlast a normal user flow (review, then Export CSV) while still bounding memory use.</summary>
    private static readonly TimeSpan ResultCacheTtl = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Constructs the controller with the three Core services it depends on
    /// plus the <see cref="IMemoryCache"/> that backs the Post-Redirect-Get
    /// flow. The calculator is intentionally not injected here —
    /// <see cref="ISolver"/> owns the calculator internally (design §4.3)
    /// and produces the <see cref="Core.Forecast.ForecastResult"/> as part
    /// of <see cref="SolverResult.Success"/>.
    /// </summary>
    public ForecastController(
        IInputValidator validator,
        ISolver solver,
        ICsvExporter csvExporter,
        IMemoryCache resultCache)
    {
        _validator = validator;
        _solver = solver;
        _csvExporter = csvExporter;
        _resultCache = resultCache;
    }

    /// <summary>
    /// Renders the input page with a fresh, empty
    /// <see cref="ForecastInputViewModel"/>. If a prior
    /// <see cref="ExportCsv"/> call failed at the solver stage, the pending
    /// TempData banner message is surfaced on
    /// <see cref="Controller.ViewData"/> for the layout to display
    /// (design §14.2).
    /// </summary>
    [HttpGet]
    public IActionResult Index()
    {
        if (TempData[ExportErrorTempDataKey] is string exportError)
        {
            ViewData[ExportErrorTempDataKey] = exportError;
        }

        if (TempData[ResultsExpiredTempDataKey] is string resultsExpired)
        {
            ViewData[ResultsExpiredTempDataKey] = resultsExpired;
        }

        return View(new ForecastInputViewModel());
    }

    /// <summary>
    /// Graceful GET fallback for <see cref="Calculate(ForecastInputViewModel)"/>.
    /// The POST action redirects to <see cref="Results"/>, so no legitimate
    /// user flow ends at <c>/Forecast/Calculate</c> under GET. This fallback
    /// still exists as a safety net for stale bookmarks that predate the
    /// Post-Redirect-Get switch — those requests get a 302 to
    /// <see cref="Index"/> instead of <c>405 Method Not Allowed</c>.
    /// </summary>
    [HttpGet]
    public IActionResult Calculate() => RedirectToAction(nameof(Index));

    /// <summary>
    /// Graceful GET fallback for <see cref="ExportCsv"/>. Same rationale as
    /// <see cref="Calculate()"/>: prevents a bare GET (bookmark, refresh, or
    /// direct navigation) from returning <c>405 Method Not Allowed</c> by
    /// redirecting to <see cref="Index"/>.
    /// </summary>
    [HttpGet]
    public IActionResult ExportCsv() => RedirectToAction(nameof(Index));

    /// <summary>
    /// Runs the validate-then-solve pipeline and, on validation success,
    /// caches the produced <see cref="ForecastResultViewModel"/> and
    /// 302-redirects to <see cref="Results"/> under the Post-Redirect-Get
    /// pattern (revised design §11.6). On validation failure the input
    /// page is re-rendered inline with preserved inputs and error messages
    /// (R2.12, R17.5); the calculator and solver MUST NOT be invoked in
    /// that path (R2.13, R27.9).
    /// </summary>
    /// <remarks>
    /// Solver failure (Requirement 15.11) uses the same PRG flow: a
    /// <see cref="ForecastResultViewModel"/> whose
    /// <see cref="ForecastResultViewModel.SolverFailureMessage"/> is
    /// populated and whose <see cref="ForecastResultViewModel.Result"/> is
    /// <see langword="null"/> is cached and redirected to
    /// <see cref="Results"/>, which renders the failure banner
    /// (Requirement 27.7).
    /// </remarks>
    /// <param name="vm">The form-bound input view model.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Calculate(ForecastInputViewModel vm)
    {
        if (!TryValidate(vm))
        {
            // Re-render Index with preserved inputs and validation messages.
            // The calculator and solver MUST NOT be invoked (R2.13, R27.9).
            return View("Index", vm);
        }

        var solverResult = _solver.Solve(vm.ToDomain());

        var resultVm = solverResult switch
        {
            SolverResult.Success success => new ForecastResultViewModel
            {
                Inputs = vm,
                Result = success.Forecast,
            },
            SolverResult.Failure failure => new ForecastResultViewModel
            {
                Inputs = vm,
                Result = null,
                SolverFailureMessage = BuildSolverFailureMessage(failure),
            },
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(SolverResult)} variant: {solverResult.GetType().Name}."),
        };

        // PRG stash: cache the produced view model under a fresh GUID and
        // hand that GUID to the browser via the redirect. The GET Results
        // action re-reads the entry on the follow-up request. The cache
        // entry is refresh-safe (repeated GETs within the TTL return the
        // same page) and self-expires so long-idle entries do not pile up.
        var id = Guid.NewGuid();
        _resultCache.Set(ResultCacheKeyPrefix + id, resultVm, ResultCacheTtl);
        return RedirectToAction(nameof(Results), new { id });
    }

    /// <summary>
    /// Renders the results page from a PRG-cached
    /// <see cref="ForecastResultViewModel"/>. The <paramref name="id"/> is
    /// the <see cref="Guid"/> stashed by
    /// <see cref="Calculate(ForecastInputViewModel)"/>. When
    /// <paramref name="id"/> is missing or the cache entry has expired,
    /// the user is redirected to <see cref="Index"/>; on expiry a
    /// TempData-carried notice is surfaced so the user understands why
    /// their bookmarked / reloaded URL is no longer live.
    /// </summary>
    /// <param name="id">The PRG cache key produced by the POST action.</param>
    [HttpGet]
    public IActionResult Results(Guid? id)
    {
        if (id is null || id == Guid.Empty)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!_resultCache.TryGetValue<ForecastResultViewModel>(
                ResultCacheKeyPrefix + id.Value,
                out var resultVm)
            || resultVm is null)
        {
            TempData[ResultsExpiredTempDataKey] =
                "Your results are no longer available. Please re-enter your inputs and click Calculate again.";
            return RedirectToAction(nameof(Index));
        }

        return View(resultVm);
    }

    /// <summary>
    /// Runs the same validate-then-solve pipeline as <see cref="Calculate"/>.
    /// On solver success returns a <c>text/csv</c> download whose filename is
    /// produced by <see cref="ICsvExporter.FileName"/> (design §12.6).
    /// On validation failure re-renders <c>Index.cshtml</c> preserving inputs
    /// and error messages. On solver failure redirects to
    /// <see cref="Index"/> with a TempData-carried banner message, refusing to
    /// emit CSV (Requirement 18.8, design §14.2).
    /// </summary>
    /// <param name="vm">The round-tripped input view model.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ExportCsv(ForecastInputViewModel vm)
    {
        if (!TryValidate(vm))
        {
            // Re-render Index with preserved inputs and validation messages.
            // The calculator and solver MUST NOT be invoked (R2.13, R27.9).
            return View("Index", vm);
        }

        var solverResult = _solver.Solve(vm.ToDomain());

        switch (solverResult)
        {
            case SolverResult.Success success:
                var csvBytes = _csvExporter.Export(success.Forecast);
                return File(
                    csvBytes,
                    "text/csv",
                    _csvExporter.FileName(DateTimeOffset.UtcNow));

            case SolverResult.Failure failure:
                TempData[ExportErrorTempDataKey] = BuildSolverFailureMessage(failure);
                return RedirectToAction(nameof(Index));

            default:
                throw new InvalidOperationException(
                    $"Unexpected {nameof(SolverResult)} variant: {solverResult.GetType().Name}.");
        }
    }

    /// <summary>
    /// Runs both validation gates against <paramref name="vm"/> and mirrors
    /// every <see cref="IInputValidator"/> error into
    /// <see cref="ControllerBase.ModelState"/> so the input view can render
    /// them via <c>asp-validation-for</c> / <c>asp-validation-summary</c>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when <see cref="ControllerBase.ModelState"/> is
    /// valid AND the domain-level validator returns
    /// <see cref="ValidationOutcome.IsValid"/> = <see langword="true"/>; the
    /// calculator/solver may then be invoked. <see langword="false"/>
    /// otherwise; the calculator and solver MUST NOT run (R2.13, R27.9).
    /// </returns>
    private bool TryValidate(ForecastInputViewModel vm)
    {
        var modelStateValid = ModelState.IsValid;

        // Cross-field and structural rules from InputValidator (design §10.3).
        // Always run these so multiple errors can be surfaced together (R17.5).
        var outcome = _validator.Validate(vm.ToDomain());

        if (!outcome.IsValid)
        {
            foreach (var error in outcome.Errors)
            {
                ModelState.AddModelError(error.FieldPath, error.Message);
            }
        }

        return modelStateValid && outcome.IsValid;
    }

    /// <summary>
    /// Formats a <see cref="SolverResult.Failure"/> for user display
    /// (design §14.2). The reason string is emitted verbatim; the safety-limit
    /// warning framing is applied by the view.
    /// </summary>
    private static string BuildSolverFailureMessage(SolverResult.Failure failure) =>
        $"The solver could not find a satisfying price within its safety limit. Reason: {failure.Reason}";
}
