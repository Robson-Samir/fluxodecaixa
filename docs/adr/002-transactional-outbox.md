# ADR-002 — Transactional Outbox para publicar eventos

**Status:** aceito

## Contexto
Ao registrar um lançamento é preciso avisar o consolidado. Gravar no banco e chamar o outro serviço são duas operações; se a segunda falhar (ou o processo morrer entre as duas), o saldo fica errado ou o lançamento é recusado sem motivo.

## Decisão
Gravar o evento numa tabela `Outbox` **na mesma transação** do lançamento. Um `BackgroundService` lê os pendentes e entrega, marcando como processado só após confirmação. Falhas geram retry com backoff exponencial (2s até 60s).

## Alternativas consideradas
- **Chamada HTTP síncrona no request:** acopla disponibilidade; viola o requisito.
- **Publicar direto no broker após o commit:** há janela em que o commit acontece e a publicação se perde (dual write).
- **CDC (Debezium):** ótimo em produção, pesado demais para rodar com um `dotnet run`.

## Consequências
- (+) Nenhum evento perdido; lançamentos independentes do consolidado.
- (−) Entrega *at-least-once* → o consumidor precisa ser idempotente (ver ADR-004).
- (−) Um worker polling a tabela (latência de ~1s e uma consulta periódica).
