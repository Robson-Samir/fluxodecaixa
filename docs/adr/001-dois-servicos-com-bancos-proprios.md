# ADR-001 — Dois serviços, cada um com seu banco

**Status:** aceito

## Contexto
O enunciado exige que o controle de lançamentos continue disponível quando o consolidado estiver fora do ar.

## Decisão
Separar em dois serviços (Lancamentos e Consolidado), cada um com banco próprio, sem acesso cruzado.

## Alternativas consideradas
- **Monolito modular:** mais simples de operar, mas uma falha ou sobrecarga no relatório derrubaria também os lançamentos. Não atende o requisito.
- **Mais serviços (auth, relatórios, gateway):** complexidade operacional sem benefício neste escopo.
- **Banco compartilhado:** a queda do banco derruba os dois, e o acoplamento por schema trava a evolução independente.

## Consequências
- (+) Isolamento de falha e escala independente (escrita × leitura).
- (−) Consistência eventual: o consolidado pode estar segundos atrás dos lançamentos.
- (−) Mais pontos de deploy e monitoramento.
