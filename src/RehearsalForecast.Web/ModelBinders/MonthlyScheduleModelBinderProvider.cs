using Microsoft.AspNetCore.Mvc.ModelBinding;
using RehearsalForecast.Web.ViewModels;

namespace RehearsalForecast.Web.ModelBinders;

/// <summary>
/// Registers <see cref="MonthlyScheduleModelBinder"/> for
/// <see cref="MonthlyScheduleViewModel"/> and
/// <see cref="OccupancyScheduleModelBinder"/> for
/// <see cref="OccupancyScheduleViewModel"/>. Both view models carry the same
/// "constant vs variable" family of shapes described in Design §9.4 and §11.4,
/// so a single provider handles both.
/// </summary>
/// <remarks>
/// Insert this provider at the front of
/// <see cref="Microsoft.AspNetCore.Mvc.MvcOptions.ModelBinderProviders"/> so it
/// runs before the default complex-type binder. Registration is performed in
/// <c>Program.cs</c> (task 64).
/// </remarks>
public sealed class MonthlyScheduleModelBinderProvider : IModelBinderProvider
{
    /// <inheritdoc />
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var modelType = context.Metadata.ModelType;

        if (modelType == typeof(MonthlyScheduleViewModel))
        {
            return new MonthlyScheduleModelBinder();
        }

        if (modelType == typeof(OccupancyScheduleViewModel))
        {
            return new OccupancyScheduleModelBinder();
        }

        return null;
    }
}
