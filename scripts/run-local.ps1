# Sobe as duas APIs em janelas separadas (Lancamentos :5101 e Consolidado :5102).
# Uso: .\scripts\run-local.ps1
$raiz = Split-Path -Parent $PSScriptRoot

Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$raiz\src\Consolidado.Api'; dotnet run"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$raiz\src\Lancamentos.Api'; dotnet run"

Write-Host "Lancamentos: http://localhost:5101"
Write-Host "Consolidado: http://localhost:5102"
Write-Host "Para testar o cenario de queda, feche a janela do Consolidado e continue lancando."
