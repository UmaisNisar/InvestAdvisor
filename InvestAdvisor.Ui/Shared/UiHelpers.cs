using FluentValidation;
using MudBlazor;

namespace InvestAdvisor.Ui.Shared;

/// <summary>Small interaction patterns every page repeats: the delete confirm and the "validate, then snackbar the first error" step.</summary>
public static class UiHelpers
{
    /// <summary>Standard destructive-action confirm. True only when the user picked Delete.</summary>
    public static async Task<bool> ConfirmDeleteAsync(this IDialogService dialogs, string title, string message)
    {
        var ok = await dialogs.ShowMessageBoxAsync(title, message, yesText: "Delete", cancelText: "Cancel");
        return ok == true;
    }

    /// <summary>
    /// Validates <paramref name="model"/> against its rules; on failure shows the inline messages
    /// (via <paramref name="form"/>) and snackbars the first error. True when the model is valid.
    /// </summary>
    public static async Task<bool> ValidateOrWarnAsync<T>(
        this AbstractValidator<T> validator, T model, MudForm? form, ISnackbar snackbar)
        where T : class
    {
        var result = await validator.ValidateAsync(model);
        if (result.IsValid) return true;
        if (form is not null) await form.ValidateAsync();
        snackbar.Add(result.Errors[0].ErrorMessage, MudBlazor.Severity.Warning);
        return false;
    }

    /// <summary>Runs a MudForm's own validation and snackbars a generic prompt when it fails.</summary>
    public static async Task<bool> ValidateOrWarnAsync(this MudForm? form, ISnackbar snackbar)
    {
        if (form is null) return true;
        await form.ValidateAsync();
        if (form.IsValid) return true;
        snackbar.Add("Fix the highlighted fields first.", MudBlazor.Severity.Warning);
        return false;
    }
}
