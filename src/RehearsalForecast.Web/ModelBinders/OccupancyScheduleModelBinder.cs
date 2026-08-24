using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Web.ViewModels;

namespace RehearsalForecast.Web.ModelBinders;

/// <summary>
/// Custom <see cref="IModelBinder"/> for
/// <see cref="OccupancyScheduleViewModel"/>. Handles the "default schedule vs
/// variable 36 rates" toggle described in Design §9.4 and Requirement 4.7.
/// </summary>
/// <remarks>
/// <para>
/// Reads <c>&lt;prefix&gt;.UseDefault</c> and
/// <c>&lt;prefix&gt;.UserRates[0..35]</c> from the form value provider.
/// <see cref="OccupancyScheduleViewModel.UseDefault"/> follows the standard
/// ASP.NET Core hidden-field-plus-checkbox convention: when the value provider
/// yields both <c>"false"</c> (from the hidden field) and <c>"true"</c> (from
/// the checked box), the binder treats the field as <see langword="true"/>.
/// When only <c>"false"</c> is present, the binder sets
/// <see cref="OccupancyScheduleViewModel.UseDefault"/> to
/// <see langword="false"/>.
/// </para>
/// <para>
/// Element-wise range checks on <see cref="OccupancyScheduleViewModel.UserRates"/>
/// (Requirement 2.10) are the responsibility of
/// <c>InputValidator</c>; this binder only parses values and records parse
/// failures against the specific month key.
/// </para>
/// </remarks>
public sealed class OccupancyScheduleModelBinder : IModelBinder
{
    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var prefix = bindingContext.ModelName;
        var model = new OccupancyScheduleViewModel();

        BindUseDefault(bindingContext, prefix, model);
        BindUserRates(bindingContext, prefix, model);

        bindingContext.Result = ModelBindingResult.Success(model);
        return Task.CompletedTask;
    }

    private static void BindUseDefault(
        ModelBindingContext bindingContext,
        string prefix,
        OccupancyScheduleViewModel model)
    {
        var key = FieldKey(prefix, nameof(OccupancyScheduleViewModel.UseDefault));
        var result = bindingContext.ValueProvider.GetValue(key);
        if (result == ValueProviderResult.None)
        {
            return;
        }

        bindingContext.ModelState.SetModelValue(key, result);

        var hasTrue = false;
        var hasFalse = false;
        var sawInvalid = false;
        string? invalidValue = null;

        foreach (var candidate in result.Values)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (bool.TryParse(candidate, out var parsed))
            {
                if (parsed)
                {
                    hasTrue = true;
                }
                else
                {
                    hasFalse = true;
                }
            }
            else
            {
                sawInvalid = true;
                invalidValue = candidate;
            }
        }

        if (hasTrue)
        {
            model.UseDefault = true;
        }
        else if (hasFalse)
        {
            model.UseDefault = false;
        }
        else if (sawInvalid)
        {
            bindingContext.ModelState.TryAddModelError(
                key,
                $"'{invalidValue}' is not a valid boolean. Expected 'true' or 'false'.");
        }
    }

    private static void BindUserRates(
        ModelBindingContext bindingContext,
        string prefix,
        OccupancyScheduleViewModel model)
    {
        var rates = new decimal[ForecastConstants.ForecastMonths];
        for (var i = 0; i < ForecastConstants.ForecastMonths; i++)
        {
            var key = FieldKey(
                prefix,
                $"{nameof(OccupancyScheduleViewModel.UserRates)}[{i}]");
            var result = bindingContext.ValueProvider.GetValue(key);
            if (result == ValueProviderResult.None)
            {
                continue;
            }

            bindingContext.ModelState.SetModelValue(key, result);

            if (TryParseDecimal(result, out var parsed))
            {
                rates[i] = parsed;
            }
            else if (!string.IsNullOrWhiteSpace(result.FirstValue))
            {
                bindingContext.ModelState.TryAddModelError(
                    key,
                    $"Month {i + 1}: '{result.FirstValue}' is not a valid number.");
            }
        }

        model.UserRates = new List<decimal>(rates);
    }

    private static string FieldKey(string prefix, string field) =>
        string.IsNullOrEmpty(prefix) ? field : $"{prefix}.{field}";

    private static bool TryParseDecimal(ValueProviderResult result, out decimal value)
    {
        value = 0m;
        var raw = result.FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var culture = result.Culture ?? CultureInfo.CurrentCulture;
        return decimal.TryParse(
            raw,
            NumberStyles.Number,
            culture,
            out value);
    }
}
