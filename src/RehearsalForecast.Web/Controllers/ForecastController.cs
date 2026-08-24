using Microsoft.AspNetCore.Mvc;
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
/// The results page round-trips the original inputs so the "Export CSV" form
/// can re-run the same pipeline on POST — the controller never persists state
/// between requests (design §11.6, Requirement 18.8).
/// </para>
/// <para>
/// When the solver breaches its safety limit (Requirement 15.11), the
/// <see cref="Calculate"/> action renders the results view with a populated
/// <see cref="ForecastResultViewModel.SolverFailureMessage"/> and a null
/// <see cref="ForecastResultViewModel.Result"/> (Requirement 27.7);
/// <see cref="ExportCsv"/> redirects back to <see cref="Index"/> with a
/// TempData-carried error message and refuses to emit CSV (design §14.2,
/// Requirement 18.8).
/// </para>
/// </remarks>
public sealed class ForecastController : Controller
{
    private readonly IInputValidator _validator;
    private readonly ISolver _solver;
    private readonly ICsvExporter _csvExporter;

    /// <summary>TempData key used by <see cref="ExportCsv"/> to surface a solver-failure banner on the redirected input page.</summary>
    internal const string ExportErrorTempDataKey = "ExportError";

    /// <summary>
    /// Constructs the controller with the three Core services it depends on.
    /// The calculator is intentionally not injected here — <see cref="ISolver"/>
    /// owns the calculator internally (design §4.3) and produces the
    /// <see cref="Core.Forecast.ForecastResult"/> as part of
    /// <see cref="SolverResult.Success"/>.
    /// </summary>
    public ForecastController(
        IInputValidator validator,
        ISolver solver,
        ICsvExporter csvExporter)
    {
        _validator = validator;
        _solver = solver;
        _csvExporter = csvExporter;
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

        return View(new ForecastInputViewModel());
    }

    /// <summary>
    /// Runs the validate-then-solve pipeline and renders either
    /// <c>Index.cshtml</c> (on validation failure, preserving inputs and error
    /// messages per R2.12 and R17.5) or <c>Results.cshtml</c> (on solver
    /// success or solver failure — the latter with
    /// <see cref="ForecastResultViewModel.SolverFailureMessage"/> populated
    /// and <see cref="ForecastResultViewModel.Result"/> null per R27.7).
    /// </summary>
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

        return solverResult switch
        {
            SolverResult.Success success => View(
                "Results",
                new ForecastResultViewModel
                {
                    Inputs = vm,
                    Result = success.Forecast,
                }),
            SolverResult.Failure failure => View(
                "Results",
                new ForecastResultViewModel
                {
                    Inputs = vm,
                    Result = null,
                    SolverFailureMessage = BuildSolverFailureMessage(failure),
                }),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(SolverResult)} variant: {solverResult.GetType().Name}."),
        };
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
