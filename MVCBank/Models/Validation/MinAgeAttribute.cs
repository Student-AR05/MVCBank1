using System;
using System.ComponentModel.DataAnnotations;

namespace MVCBank.Models.Validation
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class MinAgeAttribute : ValidationAttribute
    {
        public int Age { get; }

        public MinAgeAttribute(int age)
        {
            Age = age;
            ErrorMessage = $"Minimum age is {age} years.";
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null)
                return ValidationResult.Success; // let [Required] handle nulls

            DateTime dob;
            try
            {
                dob = Convert.ToDateTime(value);
            }
            catch
            {
                return new ValidationResult("Invalid date of birth.");
            }

            var today = DateTime.Today;
            int age = today.Year - dob.Year;
            if (dob.Date > today.AddYears(-age)) age--;

            if (age < Age)
            {
                var memberName = validationContext?.MemberName ?? string.Empty;
                var message = string.IsNullOrWhiteSpace(ErrorMessage) ? $"Minimum age is {Age} years." : ErrorMessage;
                return new ValidationResult(message, string.IsNullOrEmpty(memberName) ? null : new[] { memberName });
            }

            return ValidationResult.Success;
        }
    }
}
