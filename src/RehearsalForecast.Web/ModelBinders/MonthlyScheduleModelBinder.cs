using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Schedules;
using RehearsalForecast.Web.ViewModels;

namespace RehearsalForecast.Web.ModelBinders;

/// <summary>
/// Custom <see cref="IModelBinder"/> for
/// <see cref="MonthlyScheduleViewModel"/>. Reads
/// <c>&lt;prefix&gt;.Mode</c>, <c>&lt;prefix&gt;.ConstantValue</c>, and
/// <c>&lt;prefix&gt;.MonthlyValues[0..35]</c> from the form value provider and
/// assembles the view model in a single pass (Design §9.4, §11.4).
/// </summary>
/// <remarks>
/// <para>
/// This binder performs no range checking. Element-wise and cross-field rules
/// are enforced by <c>InputValidator</c> after the view model is mapped to the
/// domain (Requirement 2.9, 2.11). Missing values default to <c>0m</c> for
/// <see cref="MonthlyScheduleViewModel.ConstantValue"/> and each
/// <see cref="MonthlyScheduleViewModel.MonthlyValues"/> slot; a missing
/// <see cref="MonthlyScheduleViewModel.Mode"/> defaults to
/// <see cref="ScheduleMode.Constant"/> (Requirement 1.1).
/// </para>
/// <para>
/// The binder always resolves to <see cref="ModelBindingResult.Success"/>
/// carrying a fully populated view model so the controller can call
/// <c>InputValidator.Validate</c> once and surface all errors together.
/// Individual parse failures raise entries against the specific field key
/// (e.g. <c>Marketing.Advertising.MonthlyValues[7]</c>) so that
/// <c>ModelState.IsValid</c> becomes <see langword="false"/> and the failing
/// month or field is highlighted in the input page (Requirement 2.12).
/// </para>
/// </remarks>
public sealed class MonthlyScheduleModelBinder : IModelBinder
{
    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var prefix = bindingContext.ModelName;
        var model = new MonthlyScheduleViewModel();

        BindMode(bindingContext, prefix, model);
        BindConstantValue(bindingContext, prefix, model);
        BindMonthlyValues(bindingContext, prefix, model);

        bindingContext.Result = ModelBindingResult.Success(model);
        return Task.CompletedTask;
    }

    private static void BindMode(
        ModelBindingContext bindingContext,
        string prefix,
        MonthlyScheduleViewModel model)
    {
        var key = FieldKey(prefix, nameof(MonthlyScheduleViewModel.Mode));
        var result = bindingContext.ValueProvider.GetValue(key);
        if (result == ValueProviderResult.None)
        {
            return;
        }

        bindingContext.ModelState.SetModelValue(key, result);

        var raw = result.FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (Enum.TryParse<ScheduleMode>(raw, ignoreCase: true, out var mode)
            && Enum.IsDefined(mode))
        {
            model.Mode = mode;
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(
                key,
                $"'{raw}' is not a valid schedule mode. Expected 'Constant' or 'Variable'.");
        }
    }

    private static void BindConstantValue(
        ModelBindingContext bindingContext,
        string prefix,
        MonthlyScheduleViewModel model)
    {
        var key = FieldKey(prefix, nameof(MonthlyScheduleViewModel.ConstantValue));
        var result = bindingContext.ValueProvider.GetValue(key);
        if (result == ValueProviderResult.None)
        {
            return;
        }

        bindingContext.ModelState.SetModelValue(key, result);

        if (TryParseDecimal(result, out var parsed))
        {
            model.ConstantValue = parsed;
        }
        else if (!string.IsNullOrWhiteSpace(result.FirstValue))
        {
            bindingContext.ModelState.TryAddModelError(
                key,
                $"'{result.FirstValue}' is not a valid number.");
        }
    }

    private static void BindMonthlyValues(
        ModelBindingContext bindingContext,
        string prefix,
        MonthlyScheduleViewModel model)
    {
        var monthly = new decimal[ForecastConstants.ForecastMonths];
        for (var i = 0; i < ForecastConstants.ForecastMonths; i++)
        {
            var key = FieldKey(
                prefix,
                $"{nameof(MonthlyScheduleViewModel.MonthlyValues)}[{i}]");
            var result = bindingContext.ValueProvider.GetValue(key);
            if (result == ValueProviderResult.None)
            {
                continue;
            }

            bindingContext.ModelState.SetModelValue(key, result);

            if (TryParseDecimal(result, out var parsed))
            {
                monthly[i] = parsed;
            }
            else if (!string.IsNullOrWhiteSpace(result.FirstValue))
            {
                bindingContext.ModelState.TryAddModelError(
                    key,
                    $"Month {i + 1}: '{result.FirstValue}' is not a valid number.");
            }
        }

        model.MonthlyValues = new List<decimal>(monthly);
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
