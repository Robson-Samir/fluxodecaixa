# ADR-003 — HTTP como transporte dos eventos (por enquanto)

**Status:** aceito, com evolução prevista

## Contexto
O transporte "ideal" para eventos é um broker (RabbitMQ, Kafka, Azure Service Bus). Mas o desafio precisa ser executável localmente com o mínimo de pré-requisitos.

## Decisão
Entregar os eventos por `POST /internal/eventos`, protegido por API key, atrás da interface `IEventPublisher`. Com a outbox, o transporte vira um detalhe: ele já oferece retry e durabilidade, que são o que um broker daria.

## Consequências
- (+) Roda sem Docker nem infraestrutura extra.
- (+) Trocar por um broker é implementar outra `IEventPublisher` (e um consumer no consolidado); nada mais muda.
- (−) Acoplamento temporal no envio (resolvido pela outbox) e sem fan-out: se surgir um terceiro consumidor, o worker teria que conhecer todos. Esse é o gatilho para migrar para o broker.
