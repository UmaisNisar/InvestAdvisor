using FluentValidation;

namespace InvestAdvisor.Ui.Validation;

/// <summary>
/// FluentValidation validator adapted for MudBlazor forms. Pass <see cref="ValidateValue"/> to a
/// MudForm's Validation parameter and put For="@(() => model.Property)" on each input so the field
/// runs only its own property's rules (inline messages as the user types / on form Validate()).
/// </summary>
public abstract class MudCompatibleValidator<T> : AbstractValidator<T> where T : class
{
    public Func<object, string, Task<IEnumerable<string>>> ValidateValue => async (model, propertyName) =>
    {
        var result = await ValidateAsync(
            ValidationContext<T>.CreateWithOptions((T)model, x => x.IncludeProperties(propertyName)));
        return result.IsValid ? Array.Empty<string>() : result.Errors.Select(e => e.ErrorMessage);
    };
}
