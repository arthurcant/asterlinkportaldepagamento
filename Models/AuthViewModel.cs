using System.ComponentModel.DataAnnotations;

namespace asterlinkportaldepagamento.Models;

public sealed class AuthViewModel
{
    public string Mode { get; set; } = "login";

    public LoginInputModel Login { get; set; } = new();

    public RegisterInputModel Register { get; set; } = new();
}

public sealed class LoginInputModel
{
    [Required(ErrorMessage = "Informe o login ou e-mail.")]
    public string Identifier { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a senha.")]
    public string Password { get; set; } = string.Empty;
}

public sealed class RegisterInputModel
{
    [Required(ErrorMessage = "Informe seu nome.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "O nome deve ter entre 2 e 120 caracteres.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Crie um login.")]
    [StringLength(80, MinimumLength = 3, ErrorMessage = "O login deve ter entre 3 e 80 caracteres.")]
    [RegularExpression("^[a-zA-Z0-9._-]+$", ErrorMessage = "Use apenas letras, números, ponto, hífen ou sublinhado.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [StringLength(190)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Crie uma senha.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "A senha deve ter pelo menos 8 caracteres.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirme a senha.")]
    [Compare(nameof(Password), ErrorMessage = "As senhas não coincidem.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
