# Evolução e o que ficou de fora

Algumas coisas foram simplificadas de propósito para o projeto rodar com um `dotnet run`. Em ordem do que eu faria primeiro:

## Para produção

1. **Banco de servidor (PostgreSQL/SQL Server)** e **migrations do EF** no lugar de SQLite + `EnsureCreated`. O lock em memória do consolidado vira `UPDATE` atômico.
2. **Broker de mensagens** (RabbitMQ ou Kafka) no lugar do HTTP: nova implementação de `IEventPublisher` + consumer no consolidado, com DLQ para eventos que falham de forma permanente. Hoje um evento inválido nunca "morre" — fica em retry com intervalo de 60 s.
3. **Identity Provider** (Keycloak/Entra ID) emitindo os tokens; a rota `/auth/token` existe só para facilitar a execução local. Chave JWT em cofre de segredos, com RS256.
4. **API Gateway** na frente (rate limiting, TLS, roteamento) e HTTPS ponta a ponta.
5. **Cache distribuído (Redis)** quando houver mais de uma réplica do consolidado.
6. **Limpeza da outbox** (job que apaga eventos processados há mais de N dias) e métrica de tamanho da fila.

## Observabilidade
- Logs estruturados (Serilog) com *correlation id* propagado entre os serviços.
- OpenTelemetry (traces + métricas) e dashboards: tamanho da outbox, idade do evento mais antigo pendente, latência p95/p99, taxa de erro.
- Alertas: outbox crescendo por mais de X minutos indica consolidado fora.

## Funcional
- Estorno/cancelamento de lançamento (evento compensatório, mantendo o histórico imutável).
- Consolidado por período e por categoria; saldo acumulado (saldo anterior + movimento do dia).
- Suporte a múltiplos comerciantes (multi-tenant): hoje o modelo assume um único.
- Paginação na listagem de lançamentos.
- Reconstrução do consolidado a partir dos lançamentos (rotina de reconciliação).

## Qualidade
- Testes de contrato entre os serviços (o evento está duplicado nos dois projetos de propósito, para não criar acoplamento por biblioteca compartilhada — um teste de contrato garantiria que não divergem).
- Testes de carga executados em CI com o k6 e limite de regressão.
- Pipeline de CI (build, testes, análise estática) e imagens Docker com `docker-compose` para subir tudo com um comando.

## Riscos conhecidos
- Se o evento chegar com um tipo desconhecido, a API responde 400 e o worker fica reenviando. Um DLQ resolve (item 2).
- O cache de 30 s pode mostrar o saldo levemente defasado em ambiente com várias réplicas.
