using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace BTCPayServer.Hosting
{
    public class SkippableObjectValidatorProvider : ObjectModelValidator
    {
        public interface ISkipValidation
        {
            bool SkipValidation(object obj);
        }
        public class SkipValidationType<T> : ISkipValidation
        {
            public bool SkipValidation(object obj)
            {
                return obj is T;
            }
        }
        public SkippableObjectValidatorProvider(
           IModelMetadataProvider modelMetadataProvider,
           IEnumerable<ISkipValidation> skipValidations,
           IOptions<MvcOptions> mvcOptions)
           : base(modelMetadataProvider, mvcOptions.Value.ModelValidatorProviders)
        {
            _mvcOptions = mvcOptions.Value;
            SkipValidations = skipValidations.ToList();
        }

        class OverrideValidationVisitor : ValidationVisitor
        {
            public OverrideValidationVisitor(IEnumerable<ISkipValidation> skipValidations, ActionContext actionContext, IModelValidatorProvider validatorProvider, ValidatorCache validatorCache, IModelMetadataProvider metadataProvider, ValidationStateDictionary validationState) : base(actionContext, validatorProvider, validatorCache, metadataProvider, validationState)
            {
                SkipValidations = skipValidations;
            }

            public IEnumerable<ISkipValidation> SkipValidations { get; }

            protected override bool VisitComplexType(IValidationStrategy defaultStrategy)
            {
                if (SkipValidations.Any(v => v.SkipValidation(Model)))
                    return true;
                return base.VisitComplexType(defaultStrategy);
            }
        }

        public MvcOptions _mvcOptions { get; }
        IEnumerable<ISkipValidation> SkipValidations { get; }

        public override ValidationVisitor GetValidationVisitor(
            ActionContext actionContext,
            IModelValidatorProvider validatorProvider,
            ValidatorCache validatorCache,
            IModelMetadataProvider metadataProvider,
            ValidationStateDictionary validationState)
        {
            var visitor = new OverrideValidationVisitor(
                SkipValidations,
                actionContext,
                validatorProvider,
                validatorCache,
                metadataProvider,
                validationState)
            {
                MaxValidationDepth = _mvcOptions.MaxValidationDepth,
                ValidateComplexTypesIfChildValidationFails = _mvcOptions.ValidateComplexTypesIfChildValidationFails,
            };

            return visitor;
        }
    }
}
