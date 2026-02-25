

using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BeatLeader_Server.Utils {
    public sealed class NaNModelBinderProvider : IModelBinderProvider
    {
        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            var t = context.Metadata.UnderlyingOrModelType;

            if (t == typeof(double)) return new NaNSanitizingFloatingBinder<double>();
            if (t == typeof(float))  return new NaNSanitizingFloatingBinder<float>();

            return null;
        }
    }

    public sealed class NaNSanitizingFloatingBinder<T> : IModelBinder where T : struct
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            if (valueResult == ValueProviderResult.None)
                return Task.CompletedTask;

            bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);

            var raw = valueResult.FirstValue;
            if (string.IsNullOrWhiteSpace(raw))
                return Task.CompletedTask;

            // Parse
            if (typeof(T) == typeof(double))
            {
                if (!double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture, out var d))
                {
                    bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Invalid number.");
                    return Task.CompletedTask;
                }

                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    // SANITIZE:
                    if (bindingContext.ModelType == typeof(double?))
                    {
                        bindingContext.Result = ModelBindingResult.Success(null);
                    }
                    else
                    {
                        // If non-nullable, pick your policy:
                        // 1) reject:
                        bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "NaN/Infinity not allowed.");
                        // 2) OR coerce:
                        // bindingContext.Result = ModelBindingResult.Success(0d);
                    }
                    return Task.CompletedTask;
                }

                bindingContext.Result = ModelBindingResult.Success(d);
                return Task.CompletedTask;
            }

            // float
            if (!float.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out var f))
            {
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Invalid number.");
                return Task.CompletedTask;
            }

            if (float.IsNaN(f) || float.IsInfinity(f))
            {
                if (bindingContext.ModelType == typeof(float?))
                    bindingContext.Result = ModelBindingResult.Success(null);
                else
                    bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "NaN/Infinity not allowed.");

                return Task.CompletedTask;
            }

            bindingContext.Result = ModelBindingResult.Success(f);
            return Task.CompletedTask;
        }
    }
}

