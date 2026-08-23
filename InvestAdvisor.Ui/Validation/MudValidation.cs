using FluentValidation;

namespace InvestAdvisor.Ui.Validation;

/// <summary>
/// Adapts a FluentValidation validator to MudBlazor forms. Pass <c>validator.ValidateValue()</c>
/// to a MudForm's Validation parameter and put For="@(() => model.Property)" on each input so the
/// field runs only its own property's rules (inline messages as the user types / on form Validate()).
/// </summary>
public static class MudValidation
{
    public static Func<object, string, Task<IEnumerable<string>>> ValidateValue<T>(this AbstractValidator<T> validator)
        where T : class =>
        async (model, propertyName) =>
        {
            var result = await validator.ValidateAsync(
                ValidationContext<T>.CreateWithOptions((T)model, x => x.IncludeProperties(propertyName)));
            return result.IsValid ? Array.Empty<string>() : result.Errors.Select(e => e.ErrorMessage);
        };
}
