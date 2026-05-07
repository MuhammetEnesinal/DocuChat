using Microsoft.AspNetCore.Identity;

namespace DocuChat.Infrastructure.Identity;

public class TurkishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() =>
        new() { Code = nameof(DefaultError), Description = "Bilinmeyen bir hata oluştu." };

    public override IdentityError ConcurrencyFailure() =>
        new() { Code = nameof(ConcurrencyFailure), Description = "Eş zamanlılık hatası, nesne değiştirilmiş." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "Hatalı şifre." };

    public override IdentityError InvalidToken() =>
        new() { Code = nameof(InvalidToken), Description = "Geçersiz token." };

    public override IdentityError RecoveryCodeRedemptionFailed() =>
        new() { Code = nameof(RecoveryCodeRedemptionFailed), Description = "Kurtarma kodu kullanılamadı." };

    public override IdentityError LoginAlreadyAssociated() =>
        new() { Code = nameof(LoginAlreadyAssociated), Description = "Bu harici giriş zaten kullanılan bir hesaba bağlı." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = $"'{userName}' geçersiz bir kullanıcı adı. Yalnızca harf ve rakam kullanılabilir." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = $"'{email}' geçersiz bir e-posta adresi." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = $"'{userName}' kullanıcı adı zaten kullanılıyor." };

    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = $"'{email}' e-posta adresi zaten kayıtlı." };

    public override IdentityError InvalidRoleName(string? role) =>
        new() { Code = nameof(InvalidRoleName), Description = $"'{role}' geçersiz bir rol adı." };

    public override IdentityError DuplicateRoleName(string role) =>
        new() { Code = nameof(DuplicateRoleName), Description = $"'{role}' rol adı zaten kullanılıyor." };

    public override IdentityError UserAlreadyHasPassword() =>
        new() { Code = nameof(UserAlreadyHasPassword), Description = "Bu kullanıcının zaten bir şifresi var." };

    public override IdentityError UserLockoutNotEnabled() =>
        new() { Code = nameof(UserLockoutNotEnabled), Description = "Bu kullanıcı için hesap kilitleme aktif değil." };

    public override IdentityError UserAlreadyInRole(string role) =>
        new() { Code = nameof(UserAlreadyInRole), Description = $"Kullanıcı zaten '{role}' rolüne sahip." };

    public override IdentityError UserNotInRole(string role) =>
        new() { Code = nameof(UserNotInRole), Description = $"Kullanıcı '{role}' rolüne sahip değil." };

    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"Şifre en az {length} karakter olmalıdır." };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"Şifre en az {uniqueChars} farklı karakter içermelidir." };

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "Şifre en az bir özel karakter içermelidir (!@#$ vb.)." };

    public override IdentityError PasswordRequiresDigit() =>
        new() { Code = nameof(PasswordRequiresDigit), Description = "Şifre en az bir rakam (0-9) içermelidir." };

    public override IdentityError PasswordRequiresLower() =>
        new() { Code = nameof(PasswordRequiresLower), Description = "Şifre en az bir küçük harf (a-z) içermelidir." };

    public override IdentityError PasswordRequiresUpper() =>
        new() { Code = nameof(PasswordRequiresUpper), Description = "Şifre en az bir büyük harf (A-Z) içermelidir." };
}
