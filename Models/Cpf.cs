using System.ComponentModel.DataAnnotations;

namespace asterlinkportaldepagamento.Models;

public static class Cpf
{
    public static string Normalize(string? value)
    {
        return string.Concat((value ?? string.Empty).Where(IsAsciiDigit));
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !IsAsciiDigit(character) && character is not '.' and not '-'))
        {
            return false;
        }

        var digits = Normalize(value);

        if (digits.Length != 11 || digits.All(digit => digit == digits[0]))
        {
            return false;
        }

        return CalculateDigit(digits, 9) == digits[9] - '0'
            && CalculateDigit(digits, 10) == digits[10] - '0';
    }

    private static bool IsAsciiDigit(char character)
    {
        return character is >= '0' and <= '9';
    }

    private static int CalculateDigit(string digits, int length)
    {
        var sum = 0;

        for (var index = 0; index < length; index++)
        {
            sum += (digits[index] - '0') * (length + 1 - index);
        }

        var remainder = sum % 11;

        return remainder < 2 ? 0 : 11 - remainder;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class ValidCpfAttribute : ValidationAttribute
{
    public ValidCpfAttribute()
        : base("Informe um CPF válido.")
    {
    }

    public override bool IsValid(object? value)
    {
        return value is null || value is string cpf && Cpf.IsValid(cpf);
    }
}
