using NuGet.Packaging;
using NuGet.Versioning;
using System.ComponentModel.DataAnnotations;

namespace CompatFinder.Models
{
    public class PackageIdValidationAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null)
            {
                return ValidationResult.Success;
            }

            if (!(value is string strValue))
            {
                return new ValidationResult("The package ID must be a string.", new[] { validationContext.MemberName });
            }

            if (!PackageIdValidator.IsValidPackageId(strValue))
            {
                return new ValidationResult("The package ID contains invalid characters.");
            }

            return ValidationResult.Success;
        }
    }

    public class PackageVersionValidationAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null)
            {
                return ValidationResult.Success;
            }

            if (!(value is string strValue))
            {
                return new ValidationResult("The package version must be a string.", new[] { validationContext.MemberName });
            }

            if (!NuGetVersion.TryParse(strValue, out var _))
            {
                return new ValidationResult("The package version contains invalid characters.", new[] { validationContext.MemberName });
            }

            return ValidationResult.Success;
        }
    }

    public class PackageCompatibiityInput
    {
        [Required(ErrorMessage = "The package ID is required.")]
        [PackageIdValidation]
        public string Id { get; set; }

        [Required(ErrorMessage = "The package version is required.")]
        [PackageVersionValidation]
        public string Version { get; set; }

        public bool AllowEnumeration { get; set; }
    }
}
