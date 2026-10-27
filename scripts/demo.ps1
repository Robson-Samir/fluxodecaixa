# Demonstracao rapida de ponta a ponta (as duas APIs precisam estar no ar).
$lanc = "http://localhost:5101"
$cons = "http://localhost:5102"
# mesmo fuso que a API usa para definir o "dia" do lancamento
$hoje = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTime]::UtcNow, "E. South America Standard Time").ToString("yyyy-MM-dd")

$login = Invoke-RestMethod -Method Post -Uri "$lanc/auth/token" -ContentType "application/json" `
    -Body (@{ usuario = "comerciante"; senha = "senha123" } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($login.accessToken)" }

function Lancar($tipo, $valor, $desc) {
    $body = @{ tipo = $tipo; valor = $valor; descricao = $desc } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "$lanc/api/lancamentos" -Headers $h -ContentType "application/json" -Body $body | Out-Null
    Write-Host "  $tipo $valor - $desc"
}

Write-Host "Registrando lancamentos..."
Lancar "Credito" 350.00 "Vendas da manha"
Lancar "Credito" 120.50 "Vendas da tarde"
Lancar "Debito"  80.00  "Compra de embalagens"

Start-Sleep -Seconds 6   # tempo da outbox entregar os eventos
Write-Host "`nConsolidado de $hoje :"
Invoke-RestMethod -Uri "$cons/api/consolidado/$hoje" -Headers $h | ConvertTo-Json
