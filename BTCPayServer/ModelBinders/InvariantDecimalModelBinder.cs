// Copied and adjusted from https://github.com/aspnet/Mvc/blob/master/src/Microsoft.AspNetCore.Mvc.Core/ModelBinding/Binders/DecimalModelBinder.cs
using System;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BTCPayServer.ModelBinders
{
    /// <summary>
    /// An <see cref="IModelBinder"/> for <see cref="decimal"/> and <see cref="Nullable{T}"/> where <c>T</c> is
    /// <see cref="decimal"/>.
    /// </summary>
    public class InvariantDecimalModelBinder : IModelBinder
    {
        private readonly NumberStyles _supportedStyles;

        public InvariantDecimalModelBinder()
        {
            _supportedStyles = NumberStyles.Any;
        }

        /// <inheritdoc />
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
            {
                throw new ArgumentNullException(nameof(bindingContext));
            }

            var modelName = bindingContext.ModelName;
            var valueProviderResult = bindingContext.ValueProvider.GetValue(modelName);
            if (valueProviderResult == ValueProviderResult.None)
            {
                return Task.CompletedTask;
            }

            var modelState = bindingContext.ModelState;
            modelState.SetModelValue(modelName, valueProviderResult);

            var metadata = bindingContext.ModelMetadata;
            var type = metadata.UnderlyingOrModelType;
            try
            {
                var value = valueProviderResult.FirstValue;
                var culture = CultureInfo.InvariantCulture;

                object model;
                if (string.IsNullOrWhiteSpace(value))
                {
                    // Parse() method trims the value (with common NumberStyles) then throws if the result is empty.
                    model = null;
                }
                else if (type == typeof(decimal))
                {
                    model = decimal.Parse(value, _supportedStyles, culture);
                }
                else
                {
                    // unreachable
                    throw new NotSupportedException();
                }

                // When converting value, a null model may indicate a failed conversion for an otherwise required
                // model (can't set a ValueType to null). This detects if a null model value is acceptable given the
                // current bindingContext. If not, an error is logged.
                if (model == null && !metadata.IsReferenceOrNullableType)
                {
                    modelState.TryAddModelError(
                        modelName,
                        metadata.ModelBindingMessageProvider.ValueMustNotBeNullAccessor(
                            valueProviderResult.ToString()));
                }
                else
                {
                    bindingContext.Result = ModelBindingResult.Success(model);
                }
            }
            catch (Exception exception)
            {
                var isFormatException = exception is FormatException;
                if (!isFormatException && exception.InnerException != null)
                {
                    // Unlike TypeConverters, floating point types do not seem to wrap FormatExceptions. Preserve
                    // this code in case a cursory review of the CoreFx code missed something.
                    exception = ExceptionDispatchInfo.Capture(exception.InnerException).SourceException;
                }

                modelState.TryAddModelError(modelName, exception, metadata);

                // Conversion failed.
            }

            return Task.CompletedTask;
        }
    }
}
