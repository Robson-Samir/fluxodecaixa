using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Lancamentos.Api.Controllers;

public record LoginRequest(string Usuario, string Senha);

/// <summary>
/// Emissor de token simplificado, so para dar para rodar o desafio localmente.
/// Em producao isso fica num IdP (Keycloak, Entra ID...) e as APIs so validam o token.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;

    public AuthController(IConfiguration config) => _config = config;

    [HttpPost("token")]
    public IActionResult Token([FromBody] LoginRequest login)
    {
        var usuarioOk = login.Usuario == _config["Auth:Usuario"];
        var senhaOk = login.Senha == _config["Auth:Senha"];
        if (!usuarioOk || !senhaOk)
            return Unauthorized(new { erro = "Usuário ou senha inválidos." });

        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var expira = DateTime.UtcNow.AddHours(2);

        var jwt = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: [new Claim(ClaimTypes.Name, login.Usuario)],
            expires: expira,
            signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));

        return Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(jwt), expiraEmUtc = expira });
    }
}
