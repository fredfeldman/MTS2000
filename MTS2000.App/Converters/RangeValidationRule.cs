using System.Globalization;
using System.Windows.Controls;

namespace MTS2000.App.Converters;

/// <summary>Rejects text that doesn't parse as a number within [Min, Max], giving WPF's default red-border feedback.</summary>
public class RangeValidationRule : ValidationRule
{
    public double Min { get; set; } = double.MinValue;

    public double Max { get; set; } = double.MaxValue;

    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        var text = value?.ToString();
        if (!double.TryParse(text, NumberStyles.Float, cultureInfo, out var number))
        {
            return new ValidationResult(false, "Enter a valid number.");
        }

        if (number < Min || number > Max)
        {
            return new ValidationResult(false, $"Must be between {Min} and {Max}.");
        }

        return ValidationResult.ValidResult;
    }
}
