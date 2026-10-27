# ADR-004 — Consumidor idempotente e cache de leitura

**Status:** aceito

## Contexto
1. A entrega da outbox é *at-least-once*: o mesmo evento pode chegar duas vezes (ex.: o consolidado processou, mas a resposta se perdeu).
2. O consolidado deve aguentar 50 req/s de leitura com no máximo 5% de perda.

## Decisão
1. Registrar o `EventoId` numa tabela `EventosProcessados`, na mesma transação do saldo. Evento repetido responde `200` sem reaplicar.
2. Guardar o saldo do dia em `IMemoryCache` (30 s), removendo a chave quando um evento daquele dia é aplicado. Consultas a dias sem movimento retornam zerado, sem erro.

O saldo é uma soma comutativa, então a ordem de chegada dos eventos não afeta o resultado.

## Consequências
- (+) Reentrega segura; leitura quase toda servida da memória.
- (−) Com várias réplicas, cada uma tem seu cache — o dado pode ficar até 30 s defasado em uma delas. Aceitável para esse domínio; Redis resolveria se necessário.
- (−) A escrita é serializada por lock em memória (limitação do SQLite). Em banco servidor, trocar por `UPDATE ... SET total = total + @v` atômico.
